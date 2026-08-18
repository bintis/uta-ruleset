// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Game.Rulesets.Uta.Gameplay;
using osu.Game.Rulesets.Uta.Library;
using osu.Game.Rulesets.Uta.Playback;
using osu.Game.Rulesets.Uta.Queue;

namespace osu.Game.Rulesets.Uta.Remote;

public sealed class UtaRemoteCommandRouter : IUtaRemoteCommandTarget
{
    private readonly UtaSongQueueService queue;
    private readonly UtaGameplaySessionRegistry sessions;
    private readonly BindableBool autoAdvanceEnabled;
    private UtaSongLibrary? library;
    private UtaPlaybackCoordinator? playback;

    public UtaRemoteCommandRouter(UtaSongQueueService queue, UtaGameplaySessionRegistry sessions, BindableBool autoAdvanceEnabled)
    {
        this.queue = queue;
        this.sessions = sessions;
        this.autoAdvanceEnabled = autoAdvanceEnabled;
    }

    public IDisposable AttachGameplayServices(UtaSongLibrary songLibrary, UtaPlaybackCoordinator playbackCoordinator)
    {
        Volatile.Write(ref library, songLibrary);
        Volatile.Write(ref playback, playbackCoordinator);
        return new GameplayServicesLease(this, playbackCoordinator);
    }

    public UtaRemoteSnapshot GetSnapshot()
    {
        UtaRemoteSnapshot snapshot = sessions.Current?.Session.Snapshot ?? UtaRemoteSnapshot.Empty("No active Uta gameplay session.");
        return snapshot with
        {
            Queue = queue.GetSnapshot().Select(entry => new UtaRemoteQueueEntrySnapshot(
                entry.EntryId.ToString("N"), entry.Title, entry.Artist, entry.RequestedAt, entry.DifficultyName, entry.LengthMs)).ToArray(),
            AutoAdvanceEnabled = autoAdvanceEnabled.Value,
            QueueRevision = queue.Revision.Value,
            Mods = Volatile.Read(ref playback)?.GetRemoteMods() ?? Array.Empty<UtaRemoteModSnapshot>(),
        };
    }

    public UtaRemoteQueueMessage GetQueueMessage() => new(
        "queue",
        queue.Revision.Value,
        autoAdvanceEnabled.Value,
        queue.GetSnapshot().Select(entry => new UtaRemoteQueueEntrySnapshot(
            entry.EntryId.ToString("N"), entry.Title, entry.Artist, entry.RequestedAt, entry.DifficultyName, entry.LengthMs)).ToArray());

    public ValueTask<UtaRemoteCommandResult> ExecuteAsync(UtaRemoteCommand command, CancellationToken cancellationToken)
    {
        switch (command.Name)
        {
            case UtaRemoteCommands.Ping:
                return ValueTask.FromResult(UtaRemoteCommandResult.Ok());

            case UtaRemoteCommands.QueueAdd:
            case UtaRemoteCommands.QueueAddNext:
                if (!Guid.TryParse(command.Text, out Guid beatmapId))
                    return reject("A valid beatmap ID is required.");
                UtaSongLibraryEntry? song = Volatile.Read(ref library)?.Find(beatmapId);
                if (song == null)
                    return reject("This beatmap is not playable with Uta.");
                QueueMutationResult added = queue.Add(new UtaSongRequest(song.BeatmapId, song.Title, song.Artist, song.DifficultyName,
                    song.LengthMs, command.Role == UtaRemoteRole.Spectator
                        ? UtaQueueRequestSource.RemoteSpectator
                        : UtaQueueRequestSource.RemoteController));
                if (added.Succeeded && command.Name == UtaRemoteCommands.QueueAddNext)
                    queue.MoveToTop(queue.GetSnapshot().Last().EntryId);
                return ValueTask.FromResult(added.Succeeded ? UtaRemoteCommandResult.Ok() : UtaRemoteCommandResult.Reject(added.Error!));

            case UtaRemoteCommands.QueueRemove:
                return mutate(command.Text, queue.Remove);

            case UtaRemoteCommands.QueueClear:
                QueueMutationResult cleared = queue.Clear();
                return ValueTask.FromResult(cleared.Succeeded ? UtaRemoteCommandResult.Ok() : UtaRemoteCommandResult.Reject(cleared.Error!));

            case UtaRemoteCommands.QueuePlayNow:
                UtaPlaybackCoordinator? activePlayback = Volatile.Read(ref playback);
                return activePlayback == null
                    ? reject("no_active_gameplay")
                    : mutate(command.Text, activePlayback.RequestPlayNow);

            case UtaRemoteCommands.QueueMove:
                if (!Guid.TryParse(command.Text, out Guid movingId))
                    return reject("A valid queue entry ID is required.");
                QueueMutationResult moved = queue.Move(movingId, (int)command.Number!.Value);
                return ValueTask.FromResult(moved.Succeeded ? UtaRemoteCommandResult.Ok() : UtaRemoteCommandResult.Reject(moved.Error!));

            case UtaRemoteCommands.QueueMoveToTop:
                return mutate(command.Text, queue.MoveToTop);

            case UtaRemoteCommands.QueueMoveToBottom:
                return mutate(command.Text, queue.MoveToBottom);

            case UtaRemoteCommands.AutoAdvance:
                autoAdvanceEnabled.Value = command.Enabled!.Value;
                return ValueTask.FromResult(UtaRemoteCommandResult.Ok());

            case UtaRemoteCommands.SetMod:
                UtaPlaybackCoordinator? modPlayback = Volatile.Read(ref playback);
                if (modPlayback == null)
                    return reject("no_active_gameplay");
                QueueMutationResult modResult = modPlayback.SetRemoteMod(command.Text, command.Enabled!.Value);
                return ValueTask.FromResult(modResult.Succeeded ? UtaRemoteCommandResult.Ok() : UtaRemoteCommandResult.Reject(modResult.Error!));

            case UtaRemoteCommands.LibrarySearch:
                UtaSongLibrary? currentLibrary = Volatile.Read(ref library);
                if (currentLibrary == null)
                    return reject("The Uta song library has not been loaded yet.");
                return ValueTask.FromResult(new UtaRemoteCommandResult(
                    true,
                    LibraryEntries: currentLibrary.Search(command.Text).Select(song => new UtaRemoteLibraryEntrySnapshot(
                        song.BeatmapId.ToString("N"), song.Title, song.Artist, song.DifficultyName, song.Creator, song.LengthMs)).ToArray()));
        }

        IUtaGameplaySession? current = sessions.Current?.Session;
        return current == null
            ? reject("no_active_gameplay")
            : current.ExecuteAsync(command, cancellationToken);
    }

    private static ValueTask<UtaRemoteCommandResult> mutate(string? text, Func<Guid, QueueMutationResult> mutation)
    {
        if (!Guid.TryParse(text, out Guid id))
            return reject("A valid queue entry ID is required.");
        QueueMutationResult result = mutation(id);
        return ValueTask.FromResult(result.Succeeded ? UtaRemoteCommandResult.Ok() : UtaRemoteCommandResult.Reject(result.Error!));
    }

    private static ValueTask<UtaRemoteCommandResult> reject(string error)
        => ValueTask.FromResult(UtaRemoteCommandResult.Reject(error));

    private sealed class GameplayServicesLease : IDisposable
    {
        private UtaRemoteCommandRouter? owner;
        private readonly UtaPlaybackCoordinator playback;

        public GameplayServicesLease(UtaRemoteCommandRouter owner, UtaPlaybackCoordinator playback)
        {
            this.owner = owner;
            this.playback = playback;
        }

        public void Dispose()
        {
            UtaRemoteCommandRouter? currentOwner = Interlocked.Exchange(ref owner, null);
            if (currentOwner != null)
                Interlocked.CompareExchange(ref currentOwner.playback, null, playback);
        }
    }
}
