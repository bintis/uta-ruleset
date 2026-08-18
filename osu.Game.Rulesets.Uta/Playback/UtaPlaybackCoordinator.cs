// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Development;
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
    private static readonly TimeSpan in_flight_timeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan waiting_timeout = TimeSpan.FromSeconds(4);

    private readonly UtaRulesetRuntime runtime = UtaRulesetRuntime.Instance;
    private IPerformFromScreenRunner performer = null!;
    private BeatmapManager beatmapManager = null!;
    private osu.Framework.Platform.GameHost gameHost = null!;
    private Bindable<WorkingBeatmap> selectedBeatmap = null!;
    private IBindable<IReadOnlyList<Mod>> selectedMods = null!;

    [Resolved(canBeNull: true)]
    private INotificationOverlay? notifications { get; set; }

    private UtaRemoteModSnapshot[] remoteMods = Array.Empty<UtaRemoteModSnapshot>();
    private IReadOnlyList<Mod>? pendingReservationMods;

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
        runtime.Beatmaps = beatmapManager;
        selectedMods.ValueChanged += onSelectedModsChanged;
        updateRemoteMods();
        sessions.Changed += onSessionChanged;
    }

    public QueueMutationResult RequestPlayNow(Guid entryId)
        => RequestPlayNow(entryId, null);

    public QueueMutationResult RequestPlayNow(Guid entryId, UtaQueuePlaybackOptions? options)
        => onUpdateThread(() => requestPlayNowFlexible(entryId, options));

    private QueueMutationResult requestPlayNowFlexible(Guid id, UtaQueuePlaybackOptions? options)
    {
        UtaSongQueueEntry? entry = queue.GetSnapshot().FirstOrDefault(item => item.EntryId == id)
            ?? queue.GetSnapshot().LastOrDefault(item => item.BeatmapId == id && item.State == UtaQueueEntryState.Queued);

        if (entry == null)
        {
            UtaSongLibraryEntry? song = library.Find(id);
            if (song == null)
                return QueueMutationResult.Reject("The beatmap is unavailable.");

            QueueMutationResult added = queue.Add(new UtaSongRequest(
                song.BeatmapId, song.Title, song.Artist, song.DifficultyName, song.LengthMs,
                UtaQueueRequestSource.RemoteController, Options: options));
            if (!added.Succeeded)
                return added;

            entry = queue.GetSnapshot().LastOrDefault(item => item.BeatmapId == id);
            if (entry == null)
                return QueueMutationResult.Reject("The queue entry is not available.");
        }

        return requestPlayNowCore(entry.EntryId);
    }

    private void interruptTransition(string reason)
    {
        if (TransitionState.Value == UtaPlaybackTransitionState.Idle)
            return;

        osu.Framework.Logging.Logger.Log($"Uta queue interrupted {TransitionState.Value} ({reason})");
        releasePendingReservation();
        TransitionState.Value = UtaPlaybackTransitionState.Idle;
    }

    private QueueMutationResult requestPlayNowCore(Guid entryId)
    {
        if (runtime.PendingReservation?.EntryId == entryId
            && TransitionState.Value != UtaPlaybackTransitionState.Idle)
            return new QueueMutationResult(true);

        interruptTransition("play-now");

        QueueReservation? reservation = queue.Reserve(entryId);
        if (reservation == null)
        {
            if (runtime.PendingReservation?.EntryId == entryId)
                return new QueueMutationResult(true);

            return QueueMutationResult.Reject("The queue entry is not available.");
        }

        UtaSongQueueEntry? entry = queue.GetSnapshot().FirstOrDefault(item => item.EntryId == entryId);
        UtaSongLibraryEntry? song = entry == null ? null : library.Find(entry.BeatmapId);
        if (song == null)
        {
            queue.Release(reservation);
            TransitionState.Value = UtaPlaybackTransitionState.Idle;
            return QueueMutationResult.Reject("The beatmap is unavailable.");
        }

        if (sessions.Current?.Session.BeatmapId == song.BeatmapId)
        {
            queue.Commit(reservation);
            TransitionState.Value = UtaPlaybackTransitionState.Idle;
            osu.Framework.Logging.Logger.Log($"Uta queue play-now ignored; '{song.Title}' is already playing.");
            return new QueueMutationResult(true);
        }

        beginReservedNavigation(reservation, song, entry, startGameplay: true);
        return new QueueMutationResult(true);
    }

    /// <summary>
    /// Local "next song" action (N / Play next). Always leaves the current chart and
    /// starts the next queued one. Returning to song select without playing is only for
    /// the post-results path when IQ is off (<see cref="PrepareNextSelection"/>).
    /// </summary>
    public QueueMutationResult RequestSkipToNext()
        => onUpdateThread(() => requestQueuedAdvance(startGameplay: true, "skip"));

    public void PrepareNextSelection(long gameplayGeneration)
    {
        recoverStaleTransition("prepare-selection");
        if (!sessions.IsCurrent(gameplayGeneration) || TransitionState.Value != UtaPlaybackTransitionState.Idle)
            return;

        if (!tryReserveNext(out UtaSongLibraryEntry? song) || song == null)
            return;

        UtaSongQueueEntry? queued = queue.GetSnapshot().FirstOrDefault(item => item.EntryId == runtime.PendingReservation!.EntryId);
        beginReservedNavigation(runtime.PendingReservation!, song, queued, startGameplay: false);
    }

    public void RequestImmersiveAdvance(long gameplayGeneration)
    {
        if (!sessions.IsCurrent(gameplayGeneration))
        {
            osu.Framework.Logging.Logger.Log($"Uta immersive advance rejected: stale generation={gameplayGeneration}");
            return;
        }

        recoverStaleTransition("immersive-advance");
        if (TransitionState.Value != UtaPlaybackTransitionState.Idle)
        {
            osu.Framework.Logging.Logger.Log($"Uta immersive advance deferred: state={TransitionState.Value}");
            return;
        }

        QueueMutationResult result = requestQueuedAdvance(startGameplay: true, "next");
        osu.Framework.Logging.Logger.Log(result.Succeeded
            ? "Uta immersive advance accepted."
            : $"Uta immersive advance rejected: {result.Error}");
    }

    public QueueMutationResult RequestNextNow()
        => onUpdateThread(() => requestQueuedAdvance(startGameplay: true, "next"));

    internal static bool ShouldAutoplayNextSong(bool immersiveQueueEnabled, bool autoAdvanceEnabled = false)
        => immersiveQueueEnabled || autoAdvanceEnabled;

    private QueueMutationResult requestQueuedAdvance(bool startGameplay, string reason)
    {
        if (!tryBeginTransition())
            return new QueueMutationResult(true);

        if (!tryReserveNextDistinct(out UtaSongLibraryEntry? song) || song == null)
            return QueueMutationResult.Reject("The queue is empty.");

        osu.Framework.Logging.Logger.Log(
            $"Uta queue {reason}: reserved '{song.Title}' startGameplay={startGameplay} immersive={IsImmersiveQueueEnabled} autoAdvance={AutoAdvanceEnabled.Value}");
        UtaSongQueueEntry? queued = queue.GetSnapshot().FirstOrDefault(item => item.EntryId == runtime.PendingReservation!.EntryId);
        beginReservedNavigation(runtime.PendingReservation!, song, queued, startGameplay);
        return new QueueMutationResult(true);
    }

    private void beginReservedNavigation(QueueReservation reservation, UtaSongLibraryEntry song, UtaSongQueueEntry? queued, bool startGameplay)
    {
        runtime.PendingReservation = reservation;
        runtime.PendingBeatmapId = song.BeatmapId;
        setTransition(UtaPlaybackTransitionState.Reserved);
        if (sessions.Current?.Session is UtaGameplaySessionBridge live)
            live.CancelPendingAdvance();
        notify(startGameplay ? $"Next: {song.Title}" : $"Selected: {song.Title}");

        try
        {
            osu.Framework.Logging.Logger.Log($"Uta queue navigation starting '{song.Title}' startGameplay={startGameplay}");
            if (queued != null)
            {
                if (!IsDisposed)
                    applyQueuedPlayback(queued);
                else
                {
                    UtaQueuePlaybackOptions options = queued.Playback.Normalized();
                    runtime.PendingSpeed = options.Speed;
                    runtime.PendingSpeedBeatmapId = queued.BeatmapId;
                }
            }
            navigate(song, startGameplay);
        }
        catch (Exception exception)
        {
            failTransition(exception);
        }
    }

    private QueueMutationResult onUpdateThread(Func<QueueMutationResult> action)
    {
        if (gameHost == null)
            return QueueMutationResult.Reject("Playback is not ready.");

        if (ThreadSafety.IsUpdateThread || gameHost.UpdateThread.IsCurrent)
            return action();

        var completion = new TaskCompletionSource<QueueMutationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        gameHost.UpdateThread.Scheduler.Add(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetResult(QueueMutationResult.Reject(exception.GetBaseException().Message));
            }
        });

        if (!completion.Task.Wait(TimeSpan.FromSeconds(3)))
        {
            osu.Framework.Logging.Logger.Log("Uta queue update-thread marshal timed out.");
            return new QueueMutationResult(true);
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    private bool tryBeginTransition()
    {
        recoverStaleTransition("begin");
        if (TransitionState.Value == UtaPlaybackTransitionState.Idle)
            return true;

        osu.Framework.Logging.Logger.Log($"Uta queue transition still in flight: {TransitionState.Value}");
        return false;
    }

    private void recoverStaleTransition(string reason)
    {
        UtaPlaybackTransitionState state = TransitionState.Value;
        if (state == UtaPlaybackTransitionState.Idle)
            return;

        Guid? liveId = sessions.Current?.Session.BeatmapId;
        if (runtime.PendingReservation != null && liveId == runtime.PendingBeatmapId)
        {
            osu.Framework.Logging.Logger.Log($"Uta queue settled live session for pending '{runtime.PendingBeatmapId}' ({reason})");
            settleReservation(liveId.Value);
            TransitionState.Value = UtaPlaybackTransitionState.Idle;
            return;
        }

        if (!IsStaleTransition(runtime.TransitionStartedAt, state, DateTimeOffset.UtcNow))
            return;

        osu.Framework.Logging.Logger.Log($"Uta queue recovered stale transition {state} ({reason})");
        releasePendingReservation();
        TransitionState.Value = UtaPlaybackTransitionState.Idle;
    }

    internal static bool IsStaleTransition(DateTimeOffset startedAt, UtaPlaybackTransitionState state, DateTimeOffset now)
    {
        if (state == UtaPlaybackTransitionState.Idle)
            return false;
        if (state == UtaPlaybackTransitionState.Failed)
            return true;
        // A new coordinator is constructed on every Restart. Its local clock is empty;
        // that must not look older than the timeout or the just-started song is re-queued.
        if (startedAt == default)
            return false;

        TimeSpan timeout = state == UtaPlaybackTransitionState.WaitingForGameplay ? waiting_timeout : in_flight_timeout;
        return startedAt + timeout < now;
    }

    private void setTransition(UtaPlaybackTransitionState state)
    {
        TransitionState.Value = state;
        if (state == UtaPlaybackTransitionState.Idle)
            return;

        runtime.TransitionStartedAt = DateTimeOffset.UtcNow;
    }

    private void releasePendingReservation()
    {
        if (runtime.PendingReservation != null)
            queue.Release(runtime.PendingReservation);

        runtime.PendingReservation = null;
        runtime.PendingBeatmapId = Guid.Empty;
        runtime.PendingSpeed = null;
        runtime.PendingSpeedBeatmapId = Guid.Empty;
        pendingReservationMods = null;
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

    internal static bool TryComposeReservationMods(
        UtaQueuePlaybackOptions options,
        IReadOnlyList<Mod> current,
        out List<Mod> next,
        out string error)
    {
        // An empty reservation mod list means "keep what is already on". The add
        // sheet defaults every toggle off, so treating [] as a wipe killed Auto
        // and IQ the first time a queued song started.
        if (options.ModList.Count == 0)
        {
            next = current.Where(mod => mod is not UtaModTranspose).ToList();
        }
        else
        {
            next = current.Where(mod => !isReservationControlled(mod)).ToList();
            foreach (string acronym in options.ModList)
            {
                Func<Mod>? factory = remoteModFactories.FirstOrDefault(candidate => candidate().Acronym == acronym);
                if (factory == null)
                {
                    error = "Unknown or unsupported Uta mod.";
                    return false;
                }

                next.Add(factory());
            }
        }

        if (UtaModTranspose.Create(options.Transpose) is Mod transpose)
            next.Add(transpose);

        if (!ModUtils.CheckValidForGameplay(next, out _))
        {
            error = "That mod combination is not valid for gameplay.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool isReservationControlled(Mod mod)
        => mod is UtaModTranspose || remoteModFactories.Any(factory => factory().GetType() == mod.GetType());

    private QueueMutationResult applyQueuedPlayback(UtaSongQueueEntry entry)
    {
        UtaQueuePlaybackOptions options = entry.Playback.Normalized();
        runtime.PendingSpeed = options.Speed;
        runtime.PendingSpeedBeatmapId = entry.BeatmapId;

        if (selectedMods is not Bindable<IReadOnlyList<Mod>> writableMods)
            return QueueMutationResult.Reject("The global mod selection is unavailable.");

        if (!TryComposeReservationMods(options, selectedMods.Value, out List<Mod> next, out string error))
            return QueueMutationResult.Reject(error);

        pendingReservationMods = next;
        flushPendingReservationMods();
        return new QueueMutationResult(true);
    }

    private void flushPendingReservationMods()
    {
        if (pendingReservationMods == null)
            return;

        if (selectedMods is not Bindable<IReadOnlyList<Mod>> writableMods || writableMods.Disabled)
        {
            osu.Framework.Logging.Logger.Log("Uta queue deferred reservation mods; selection is locked.");
            return;
        }

        writableMods.Value = pendingReservationMods;
        pendingReservationMods = null;
        osu.Framework.Logging.Logger.Log("Uta queue applied reservation mods.");
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

    private bool tryReserveNextDistinct(out UtaSongLibraryEntry? song)
    {
        song = null;
        Guid? playing = sessions.Current?.Session.BeatmapId;
        for (int i = 0; i < 8; i++)
        {
            if (!tryReserveNext(out song) || song == null)
                return false;

            if (playing == null || song.BeatmapId != playing)
                return true;

            osu.Framework.Logging.Logger.Log($"Uta queue dropped already-playing '{song.Title}'");
            queue.Commit(runtime.PendingReservation!);
            runtime.PendingReservation = null;
            runtime.PendingBeatmapId = Guid.Empty;
            song = null;
        }

        return false;
    }

    private void navigate(UtaSongLibraryEntry song, bool startGameplay)
    {
        try
        {
            setTransition(UtaPlaybackTransitionState.Navigating);
            WorkingBeatmap targetBeatmap = beatmapManager.GetWorkingBeatmap(song.Beatmap);
            ensureTrackLoaded(targetBeatmap);
            runtime.RememberPlayedBeatmap(targetBeatmap);

            // Always restart the existing Player, including when it is sitting under
            // results. PerformFromScreen(SongSelect) resumes SongSelect, which calls
            // PrepareTrackForPreview on a WorkingBeatmap with no track and freezes.
            if (startGameplay && sessions.Current?.Session is UtaGameplaySessionBridge liveSession)
            {
                if (liveSession.TryRestartWith(targetBeatmap))
                {
                    settleReservation(song.BeatmapId);
                    setTransition(UtaPlaybackTransitionState.WaitingForGameplay);
                    osu.Framework.Logging.Logger.Log($"Uta queue restart accepted for '{song.Title}'");
                    return;
                }

                throw new InvalidOperationException(
                    $"Could not restart the player into '{song.Title}'.");
            }

            runtime.EnsureAllPreviewTracks();
            ensureTrackLoaded(targetBeatmap);

            bool inGameplay = sessions.Current != null;
            bool currentTrackLoaded = tryEnsureCurrentTrackLoaded();
            osu.Framework.Logging.Logger.Log(
                $"Uta queue navigation via song select: target={song.BeatmapId} '{song.Title}' "
                + $"inGameplay={inGameplay} currentTrackLoaded={currentTrackLoaded}");

            if (inGameplay && !currentTrackLoaded)
                throw new InvalidOperationException($"Refusing to leave gameplay; the current beatmap has no track.");

            setTransition(startGameplay
                ? UtaPlaybackTransitionState.WaitingForGameplay
                : UtaPlaybackTransitionState.Navigating);
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

            flushPendingReservationMods();

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
            settleReservation(song.BeatmapId);
            setTransition(UtaPlaybackTransitionState.WaitingForGameplay);
            osu.Framework.Logging.Logger.Log($"Uta queue navigation started gameplay for '{song.Title}'");
        }
        catch (Exception exception)
        {
            failTransition(exception);
        }
    }

    private bool tryEnsureCurrentTrackLoaded()
    {
        try
        {
            if (selectedBeatmap == null || selectedBeatmap.Disabled)
                return false;

            ensureTrackLoaded(selectedBeatmap.Value);
            return selectedBeatmap.Value.TrackLoaded;
        }
        catch (Exception exception)
        {
            osu.Framework.Logging.Logger.Log($"Uta queue could not inspect the current beatmap: {exception.Message}");
            return false;
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
        setTransition(UtaPlaybackTransitionState.Idle);
        osu.Framework.Logging.Logger.Log("Uta queue navigation selected the next song without starting gameplay.");
    }

    private void onSessionChanged()
    {
        if (ThreadSafety.IsUpdateThread)
            handleSessionChanged();
        else
            gameHost.UpdateThread.Scheduler.Add(handleSessionChanged);
    }

    private void handleSessionChanged()
    {
        GameplayLease? current = sessions.Current;
        if (current == null)
        {
            runtime.PrepareSongSelectPreview();
            gameHost.UpdateThread.Scheduler.AddDelayed(runtime.PrepareSongSelectPreview, 0);
            return;
        }

        if (runtime.PendingReservation != null)
        {
            settleReservation(current.Session.BeatmapId);
            TransitionState.Value = UtaPlaybackTransitionState.Idle;
        }
        else if (TransitionState.Value == UtaPlaybackTransitionState.WaitingForGameplay)
        {
            TransitionState.Value = UtaPlaybackTransitionState.Idle;
        }

        flushPendingReservationMods();
        applyPendingSpeed(current.Session);
    }

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

    private void applyPendingSpeed(IUtaGameplaySession session)
    {
        double? speed = runtime.PendingSpeed;
        if (speed == null)
            return;
        if (runtime.PendingSpeedBeatmapId != Guid.Empty && session.BeatmapId != runtime.PendingSpeedBeatmapId)
            return;

        runtime.PendingSpeed = null;
        runtime.PendingSpeedBeatmapId = Guid.Empty;
        if (Math.Abs(speed.Value - 1) < 0.0001)
            return;

        _ = session.ExecuteAsync(
            new UtaRemoteCommand(0, UtaRemoteCommands.Speed, speed, null, null),
            CancellationToken.None);
    }

    private void failTransition(Exception? exception = null)
    {
        if (exception != null)
            osu.Framework.Logging.Logger.Log($"Uta queue navigation failed: {exception}");

        releasePendingReservation();
        TransitionState.Value = UtaPlaybackTransitionState.Idle;
        notify("Could not start the next queued song.");
    }

    protected override void Update()
    {
        base.Update();
        recoverStaleTransition("watchdog");
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
