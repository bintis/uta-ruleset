// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Uta.Gameplay;
using osu.Game.Rulesets.Uta.Library;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Queue;
using osu.Game.Rulesets.Uta.Remote;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens;
using osu.Game.Screens.Select;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Uta.Playback;

public enum UtaPlaybackTransitionState
{
    Idle,
    Reserved,
    Navigating,
    WaitingForGameplay,
    Failed,
}

public sealed partial class UtaPlaybackCoordinator : Component
{
    private readonly UtaSongQueueService queue;
    private readonly UtaSongLibrary library;
    private readonly UtaGameplaySessionRegistry sessions;

    private const int start_retry_delay_ms = 80;
    private const int start_retry_attempts = 25;
    private const int play_after_song_select_delay_ms = 350;

    private readonly UtaRulesetRuntime runtime = UtaRulesetRuntime.Instance;
    private IPerformFromScreenRunner performer = null!;
    private BeatmapManager beatmapManager = null!;
    private osu.Framework.Platform.GameHost gameHost = null!;
    private Bindable<WorkingBeatmap> selectedBeatmap = null!;
    private IBindable<IReadOnlyList<Mod>> selectedMods = null!;

    [Resolved(canBeNull: true)]
    private INotificationOverlay? notifications { get; set; }

    private UtaRemoteModSnapshot[] remoteMods = Array.Empty<UtaRemoteModSnapshot>();

    public Bindable<UtaPlaybackTransitionState> TransitionState => runtime.TransitionState;
    public BindableBool AutoAdvanceEnabled { get; }
    public bool IsImmersiveQueueEnabled => selectedMods?.Value.Any(mod => mod is UtaModImmersiveQueue) == true;

    public UtaPlaybackCoordinator(
        UtaSongQueueService queue,
        UtaSongLibrary library,
        UtaGameplaySessionRegistry sessions,
        BindableBool? autoAdvanceEnabled = null)
    {
        this.queue = queue;
        this.library = library;
        this.sessions = sessions;
        AutoAdvanceEnabled = autoAdvanceEnabled ?? new BindableBool();
    }

    [BackgroundDependencyLoader]
    private void load(
        IPerformFromScreenRunner performer,
        BeatmapManager beatmapManager,
        osu.Framework.Platform.GameHost gameHost,
        Bindable<WorkingBeatmap> selectedBeatmap,
        IBindable<IReadOnlyList<Mod>> selectedMods)
    {
        this.performer = performer;
        this.beatmapManager = beatmapManager;
        this.gameHost = gameHost;
        this.selectedBeatmap = selectedBeatmap;
        this.selectedMods = selectedMods;
        selectedMods.ValueChanged += onSelectedModsChanged;
        updateRemoteMods();
        sessions.Changed += onSessionChanged;
    }

    public QueueMutationResult RequestPlayNow(Guid entryId)
    {
        if (TransitionState.Value is UtaPlaybackTransitionState.Reserved or UtaPlaybackTransitionState.Navigating or UtaPlaybackTransitionState.WaitingForGameplay)
            return QueueMutationResult.Reject("transition_busy");

        QueueReservation? reservation = queue.Reserve(entryId);
        if (reservation == null)
            return QueueMutationResult.Reject("The queue entry is not available.");

        UtaSongQueueEntry? entry = queue.GetSnapshot().FirstOrDefault(item => item.EntryId == entryId);
        UtaSongLibraryEntry? song = entry == null ? null : library.Find(entry.BeatmapId);
        if (song == null)
        {
            queue.Release(reservation);
            return QueueMutationResult.Reject("The beatmap is unavailable.");
        }

        beginReservedNavigation(reservation, song, startGameplay: true);
        return new QueueMutationResult(true);
    }

    /// <summary>
    /// Local "next song" action (N / Play next). Always leaves the current chart and
    /// starts the next queued one. Returning to song select without playing is only for
    /// the post-results path when IQ is off (<see cref="PrepareNextSelection"/>).
    /// </summary>
    public QueueMutationResult RequestSkipToNext()
        => requestQueuedAdvance(startGameplay: true, "skip");

    public void PrepareNextSelection(long gameplayGeneration)
    {
        if (!sessions.IsCurrent(gameplayGeneration) || TransitionState.Value != UtaPlaybackTransitionState.Idle)
            return;

        if (!tryReserveNext(out UtaSongLibraryEntry? song) || song == null)
            return;

        beginReservedNavigation(runtime.PendingReservation!, song, startGameplay: false);
    }

    public void RequestImmersiveAdvance(long gameplayGeneration)
    {
        if (!sessions.IsCurrent(gameplayGeneration))
        {
            osu.Framework.Logging.Logger.Log($"Uta immersive advance rejected: stale generation={gameplayGeneration}");
            return;
        }

        if (TransitionState.Value != UtaPlaybackTransitionState.Idle)
        {
            osu.Framework.Logging.Logger.Log($"Uta immersive advance rejected: state={TransitionState.Value}");
            return;
        }

        QueueMutationResult result = RequestNextNow();
        osu.Framework.Logging.Logger.Log(result.Succeeded
            ? "Uta immersive advance accepted."
            : $"Uta immersive advance rejected: {result.Error}");
    }

    public QueueMutationResult RequestNextNow()
        => requestQueuedAdvance(startGameplay: true, "next");

    internal static bool ShouldAutoplayNextSong(bool immersiveQueueEnabled) => immersiveQueueEnabled;

    private QueueMutationResult requestQueuedAdvance(bool startGameplay, string reason)
    {
        if (sessions.Current == null)
            return QueueMutationResult.Reject("no_active_gameplay");

        if (TransitionState.Value != UtaPlaybackTransitionState.Idle)
            return QueueMutationResult.Reject("transition_busy");

        if (!tryReserveNext(out UtaSongLibraryEntry? song) || song == null)
            return QueueMutationResult.Reject("The queue is empty.");

        osu.Framework.Logging.Logger.Log(
            $"Uta queue {reason}: reserved '{song.Title}' startGameplay={startGameplay} immersive={IsImmersiveQueueEnabled}");
        beginReservedNavigation(runtime.PendingReservation!, song, startGameplay);
        return new QueueMutationResult(true);
    }

    private void beginReservedNavigation(QueueReservation reservation, UtaSongLibraryEntry song, bool startGameplay)
    {
        runtime.PendingReservation = reservation;
        runtime.PendingBeatmapId = song.BeatmapId;
        TransitionState.Value = UtaPlaybackTransitionState.Reserved;
        notify(startGameplay ? $"Next: {song.Title}" : $"Selected: {song.Title}");
        gameHost.UpdateThread.Scheduler.Add(() => navigate(song, startGameplay));
    }

    public QueueMutationResult RequestEndCurrent()
    {
        GameplayLease? current = sessions.Current;
        if (current == null)
            return QueueMutationResult.Reject("no_active_gameplay");

        _ = current.Session.ExecuteAsync(
            new UtaRemoteCommand(0, UtaRemoteCommands.SkipCurrent, null, null, null),
            CancellationToken.None);
        return new QueueMutationResult(true);
    }

    public IReadOnlyList<UtaRemoteModSnapshot> GetRemoteMods() => Volatile.Read(ref remoteMods);

    public QueueMutationResult SetRemoteMod(string? acronym, bool enabled)
    {
        if (selectedMods is not Bindable<IReadOnlyList<Mod>> writableMods)
            return QueueMutationResult.Reject("The global mod selection is unavailable.");

        Func<Mod>? factory = remoteModFactories.FirstOrDefault(candidate => candidate().Acronym == acronym);
        if (factory == null)
            return QueueMutationResult.Reject("Unknown or unsupported Uta mod.");

        Mod requested = factory();
        List<Mod> next = selectedMods.Value.Where(mod => mod.GetType() != requested.GetType()).ToList();
        if (enabled)
            next.Add(requested);

        if (!ModUtils.CheckValidForGameplay(next, out _))
            return QueueMutationResult.Reject("That mod combination is not valid for gameplay.");

        gameHost.UpdateThread.Scheduler.Add(() => writableMods.Value = next);
        return new QueueMutationResult(true);
    }

    private static readonly Func<Mod>[] remoteModFactories =
    {
        () => new UtaModImmersiveQueue(),
        () => new UtaModNoFail(),
        () => new UtaModRelax(),
        () => new UtaModOriginalVocals(),
        () => new UtaModOctaveFold(),
        () => new UtaModHidePitchGuide(),
        () => new UtaModHideLyrics(),
        () => new UtaModAutoplay(),
        () => new UtaModRecording(),
        () => new UtaModPractice(),
    };

    private void onSelectedModsChanged(ValueChangedEvent<IReadOnlyList<Mod>> change) => updateRemoteMods();

    private void updateRemoteMods()
    {
        IReadOnlyList<Mod> selected = selectedMods.Value;
        Volatile.Write(ref remoteMods, remoteModFactories.Select(factory =>
        {
            Mod mod = factory();
            return new UtaRemoteModSnapshot(mod.Acronym, mod.Name, selected.Any(active => active.GetType() == mod.GetType()));
        }).ToArray());
    }

    private bool tryReserveNext(out UtaSongLibraryEntry? song)
    {
        song = null;
        QueueReservation? reservation = queue.ReserveNext();
        if (reservation == null)
            return false;

        UtaSongQueueEntry? entry = queue.GetSnapshot().FirstOrDefault(item => item.EntryId == reservation.EntryId);
        song = entry == null ? null : library.Find(entry.BeatmapId);
        if (song == null)
        {
            queue.Release(reservation);
            return false;
        }

        runtime.PendingReservation = reservation;
        runtime.PendingBeatmapId = song.BeatmapId;
        return true;
    }

    private void navigate(UtaSongLibraryEntry song, bool startGameplay)
    {
        try
        {
            TransitionState.Value = UtaPlaybackTransitionState.Navigating;
            WorkingBeatmap targetBeatmap = beatmapManager.GetWorkingBeatmap(song.Beatmap);
            ensureTrackLoaded(targetBeatmap);
            runtime.RememberPlayedBeatmap(targetBeatmap);

            // Always restart through the *current* gameplay session. The coordinator's
            // resolved Player/lease die after the previous Restart, and falling back
            // to SongSelect then freezes on PrepareTrackForPreview.
            if (startGameplay)
            {
                if (sessions.Current?.Session is UtaGameplaySessionBridge liveSession
                    && liveSession.TryRestartWith(targetBeatmap))
                {
                    TransitionState.Value = UtaPlaybackTransitionState.WaitingForGameplay;
                    osu.Framework.Logging.Logger.Log($"Uta queue restart accepted for '{song.Title}'");
                    return;
                }

                throw new InvalidOperationException(
                    $"Could not restart the current player into '{song.Title}'.");
            }

            ensureTrackLoaded(selectedBeatmap.Value);
            osu.Framework.Logging.Logger.Log(
                $"Uta queue navigation via song select: target={song.BeatmapId} '{song.Title}' "
                + $"current={selectedBeatmap.Value.BeatmapInfo.ID} "
                + $"disabled={selectedBeatmap.Disabled} exitTrackLoaded={selectedBeatmap.Value.TrackLoaded}");

            if (!selectedBeatmap.Value.TrackLoaded)
                throw new InvalidOperationException($"Refusing to leave gameplay; '{selectedBeatmap.Value}' has no track.");

            performer.PerformFromScreen(
                screen =>
                {
                    try
                    {
                        if (screen is not SongSelect songSelect || screen is not IHandlePresentBeatmap beatmapPresenter)
                            throw new InvalidOperationException("The queued beatmap can only be started from song select.");

                        gameHost.UpdateThread.Scheduler.AddDelayed(
                            () => continueAfterSongSelect(songSelect, beatmapPresenter, song, targetBeatmap, startGameplay, 0),
                            play_after_song_select_delay_ms);
                    }
                    catch (Exception exception)
                    {
                        failTransition(exception);
                    }
                },
                new[] { typeof(SongSelect) });
        }
        catch (Exception exception)
        {
            failTransition(exception);
        }
    }

    private void continueAfterSongSelect(
        SongSelect songSelect,
        IHandlePresentBeatmap beatmapPresenter,
        UtaSongLibraryEntry song,
        WorkingBeatmap targetBeatmap,
        bool startGameplay,
        int attempt)
    {
        try
        {
            if (!songSelect.IsCurrentScreen())
            {
                retryOrFail(songSelect, beatmapPresenter, song, targetBeatmap, startGameplay, attempt, "Song select is no longer current.");
                return;
            }

            if (songSelect.Beatmap.Disabled)
            {
                retryOrFail(songSelect, beatmapPresenter, song, targetBeatmap, startGameplay, attempt, "Song select beatmap is still leased.");
                return;
            }

            if (songSelect.Beatmap.Value.BeatmapInfo.ID != song.BeatmapId)
                beatmapPresenter.PresentBeatmap(targetBeatmap, song.Beatmap.Ruleset);

            ensureTrackLoaded(songSelect.Beatmap.Value);

            osu.Framework.Logging.Logger.Log(
                $"Uta queue navigation ready-check attempt={attempt} songSelect={songSelect.Beatmap.Value.BeatmapInfo.ID} "
                + $"target={song.BeatmapId} disabled={songSelect.Beatmap.Disabled} "
                + $"trackLoaded={songSelect.Beatmap.Value.TrackLoaded}");

            if (!songSelect.Beatmap.Value.TrackLoaded)
            {
                retryOrFail(songSelect, beatmapPresenter, song, targetBeatmap, startGameplay, attempt, "Song select beatmap has no track.");
                return;
            }

            if (!startGameplay)
            {
                completeSelectionWithoutGameplay();
                return;
            }

            var playAction = songSelect.GetForwardActions(song.Beatmap)
                                       .FirstOrDefault(item => item.Action.Value != null);

            if (playAction?.Action.Value == null)
                throw new InvalidOperationException("Song select did not provide a play action for the queued beatmap.");

            playAction.Action.Value.Invoke();
            TransitionState.Value = UtaPlaybackTransitionState.WaitingForGameplay;
            osu.Framework.Logging.Logger.Log($"Uta queue navigation started gameplay for '{song.Title}'");
        }
        catch (Exception exception)
        {
            failTransition(exception);
        }
    }

    private static void ensureTrackLoaded(WorkingBeatmap beatmap)
    {
        if (beatmap.TrackLoaded)
            return;

        try
        {
            beatmap.LoadTrack();
        }
        catch (Exception exception)
        {
            osu.Framework.Logging.Logger.Log($"Uta queue navigation could not load track for '{beatmap}': {exception.Message}");
        }
    }

    private void retryOrFail(
        SongSelect songSelect,
        IHandlePresentBeatmap beatmapPresenter,
        UtaSongLibraryEntry song,
        WorkingBeatmap targetBeatmap,
        bool startGameplay,
        int attempt,
        string reason)
    {
        if (attempt + 1 >= start_retry_attempts)
        {
            if (!startGameplay)
            {
                completeSelectionWithoutGameplay();
                return;
            }

            throw new InvalidOperationException($"Timed out waiting to start the next song: {reason}");
        }

        gameHost.UpdateThread.Scheduler.AddDelayed(
            () => continueAfterSongSelect(songSelect, beatmapPresenter, song, targetBeatmap, startGameplay, attempt + 1),
            start_retry_delay_ms);
    }

    private void completeSelectionWithoutGameplay()
    {
        settleReservation(runtime.PendingBeatmapId);
        TransitionState.Value = UtaPlaybackTransitionState.Idle;
        osu.Framework.Logging.Logger.Log("Uta queue navigation selected the next song without starting gameplay.");
    }

    private void onSessionChanged() => gameHost.UpdateThread.Scheduler.Add(() =>
    {
        GameplayLease? current = sessions.Current;
        if (current == null)
        {
            runtime.PrepareSongSelectPreview();
            gameHost.UpdateThread.Scheduler.AddDelayed(runtime.PrepareSongSelectPreview, 0);
            return;
        }

        if (runtime.PendingReservation == null)
            return;

        settleReservation(current.Session.BeatmapId);
        TransitionState.Value = UtaPlaybackTransitionState.Idle;
    });

    private void settleReservation(Guid actualBeatmapId)
    {
        QueueReservation? reservation = runtime.PendingReservation;
        if (reservation == null)
            return;

        if (actualBeatmapId == runtime.PendingBeatmapId)
            queue.Commit(reservation);
        else
            queue.Release(reservation);

        runtime.PendingReservation = null;
        runtime.PendingBeatmapId = Guid.Empty;
    }

    private void failTransition(Exception? exception = null)
    {
        if (exception != null)
            osu.Framework.Logging.Logger.Log($"Uta queue navigation failed: {exception}");

        if (runtime.PendingReservation != null)
            queue.Release(runtime.PendingReservation);
        runtime.PendingReservation = null;
        runtime.PendingBeatmapId = Guid.Empty;
        TransitionState.Value = UtaPlaybackTransitionState.Failed;
        notify("Could not start the next queued song.");
    }

    private void notify(string text)
    {
        osu.Framework.Logging.Logger.Log($"Uta queue notice: {text}");
        notifications?.Post(new SimpleNotification { Text = text });
    }

    protected override void Dispose(bool isDisposing)
    {
        sessions.Changed -= onSessionChanged;
        if (selectedMods != null)
            selectedMods.ValueChanged -= onSelectedModsChanged;
        base.Dispose(isDisposing);
    }
}
