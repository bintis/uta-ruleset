// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Playback;
using osu.Game.Rulesets.Uta.Queue;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Rulesets.Uta.UI;
using osu.Game.Rulesets.Uta.Remote;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.Gameplay;

/// <summary>
/// Gameplay-thread boundary for remote commands. Network threads never touch
/// osu! bindables or the gameplay clock directly.
/// </summary>
internal sealed partial class UtaGameplaySessionBridge : Component, IUtaGameplaySession
{
    private static readonly long snapshot_interval_ticks = Stopwatch.Frequency / 10;
    private bool leftGameplay;

    private readonly UtaBeatmap beatmap;
    private readonly bool immersiveQueue;
    private readonly double songLength;

    private GameplayClockContainer gameplayClock = null!;
    private DrawableRuleset drawableRuleset = null!;
    private UtaPracticeController practice = null!;
    private UtaAudioSettingsState audio = null!;
    private UtaInputManager input = null!;
    private UtaRuntimeModeState modes = null!;
    private UtaGameplayScoringController scoringController = null!;
    private osu.Framework.Platform.GameHost gameHost = null!;
    private UtaScoreProcessor? score;
    private bool debugDiagnostics;
    private bool remoteSkipInProgress;
    private long revision;
    private long lastSnapshotCaptureTimestamp;
    private UtaRemoteSnapshot latestSnapshot;
    private GameplayLease? lease;
    private UtaGameplaySessionRegistry sessions = null!;
    private UtaPlaybackCoordinator playback = null!;
    private IBindable<WorkingBeatmap> selectedBeatmap = null!;
    private readonly CancellationTokenSource immersiveAdvanceCancellation = new();

    private static readonly MethodInfo? progress_to_results = typeof(Player).GetMethod(
        "progressToResults",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        types: new[] { typeof(bool) },
        modifiers: null);

    public Guid BeatmapId => beatmap.BeatmapInfo.ID;
    public UtaRemoteSnapshot Snapshot => Volatile.Read(ref latestSnapshot);

    public UtaGameplaySessionBridge(UtaBeatmap beatmap, bool immersiveQueue)
    {
        this.beatmap = beatmap;
        this.immersiveQueue = immersiveQueue;
        songLength = calculateSongLength(beatmap);
        latestSnapshot = createEmptySnapshot();
    }

    [Resolved(canBeNull: true)]
    private Player? player { get; set; }

    [BackgroundDependencyLoader]
    private void load(
        GameplayClockContainer gameplayClock,
        DrawableRuleset drawableRuleset,
        UtaPracticeController practice,
        UtaAudioSettingsState audio,
        UtaInputManager input,
        UtaRuntimeModeState modes,
        UtaGameplayScoringController scoringController,
        osu.Framework.Platform.GameHost gameHost,
        UtaGameplaySessionRegistry sessions,
        UtaPlaybackCoordinator playback,
        IBindable<WorkingBeatmap> selectedBeatmap,
        ScoreProcessor scoreProcessor,
        UtaRulesetConfigManager config)
    {
        this.gameplayClock = gameplayClock;
        this.drawableRuleset = drawableRuleset;
        this.practice = practice;
        this.audio = audio;
        this.input = input;
        this.modes = modes;
        this.scoringController = scoringController;
        this.gameHost = gameHost;
        this.sessions = sessions;
        this.playback = playback;
        this.selectedBeatmap = selectedBeatmap;
        score = scoreProcessor as UtaScoreProcessor;
        debugDiagnostics = config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics).Value;
        gameplayClock.IsPaused.BindValueChanged(onGameplayPausedChanged);
    }

    private void onGameplayPausedChanged(ValueChangedEvent<bool> paused)
    {
        if (!paused.NewValue)
            return;

        if (player != null)
            UtaRulesetRuntime.Instance.RememberPlayedBeatmap(player.Beatmap.Value);
    }

    protected override void Update()
    {
        base.Update();
        if (!leftGameplay && player != null && !player.IsCurrentScreen())
        {
            leftGameplay = true;
            // Same teardown as QL/F7 LeaveForQueuedChart: destroy mixers, then
            // silence MusicController so song select cannot resume chart A.
            UtaAudioController.DestroyAllPlayback();
            UtaRulesetRuntime.Instance.StopLeftoverOnLeave();
        }

        long now = Stopwatch.GetTimestamp();
        if (now - lastSnapshotCaptureTimestamp < snapshot_interval_ticks)
            return;

        lastSnapshotCaptureTimestamp = now;
        Volatile.Write(ref latestSnapshot, captureSnapshot());
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        lease = sessions.Attach(this);
        if (player != null)
        {
            player.OnShowingResults += onShowingResults;
            UtaRulesetRuntime.Instance.RememberPlayedBeatmap(player.Beatmap.Value);
        }
    }

    /// <summary>
    /// Leave the current player so the queue can start <paramref name="target"/>
    /// from song select. Player.Restart keeps PlayerLoader and races the leased
    /// beatmap bindable, which loads the previous chart with the next song's audio.
    /// </summary>
    internal bool LeaveForQueuedChart(WorkingBeatmap target)
    {
        Player? currentPlayer = player;
        if (currentPlayer == null)
        {
            osu.Framework.Logging.Logger.Log($"Uta queue leave skipped: player=null beatmap={target.BeatmapInfo.ID}");
            return false;
        }

        UtaRulesetRuntime.Instance.RememberPlayedBeatmap(target);
        UtaRulesetRuntime.Instance.TryPublishWorkingBeatmap(target);

        if (!target.TrackLoaded)
        {
            try
            {
                target.LoadTrack();
            }
            catch (Exception exception)
            {
                osu.Framework.Logging.Logger.Log($"Uta queue leave track load failed: {exception.Message}");
                return false;
            }
        }

        osu.Framework.Logging.Logger.Log($"Uta queue leaving gameplay for {target.BeatmapInfo.ID}");
        UtaAudioController.DestroyAllPlayback();

        // Results is a child of Player. Remote queue commands remain available on
        // that screen, but ScreenStack rejects Player.Exit while the child exists.
        // Make Player current first (popping Results), then leave it normally.
        if (!currentPlayer.IsCurrentScreen())
            currentPlayer.MakeCurrent();
        currentPlayer.Exit();
        return true;
    }

    internal void CancelPendingAdvance()
    {
        try
        {
            immersiveAdvanceCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void onShowingResults()
    {
        long generation = lease?.Generation ?? -1;
        bool immersive = immersiveQueue || playback.IsImmersiveQueueEnabled;
        bool autoAdvance = playback.AutoAdvanceEnabled.Value;
        bool shouldAdvance = UtaPlaybackCoordinator.ShouldAutoplayNextSong(immersive, autoAdvance);
        osu.Framework.Logging.Logger.Log(
            $"Uta results shown: generation={generation} immersive={immersive} autoAdvance={autoAdvance} playNext={shouldAdvance}");
        if (shouldAdvance)
            _ = scheduleImmersiveAdvanceAsync(generation);
        else
            Schedule(() => playback.PrepareNextSelection(generation));
    }

    private async Task scheduleImmersiveAdvanceAsync(long generation)
    {
        try
        {
            await Task.Delay(3000, immersiveAdvanceCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        gameHost.UpdateThread.Scheduler.Add(() => playback.RequestImmersiveAdvance(generation));
    }

    public ValueTask<UtaRemoteCommandResult> ExecuteAsync(UtaRemoteCommand command, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<UtaRemoteCommandResult>(cancellationToken);

        var completion = new TaskCompletionSource<UtaRemoteCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        Schedule(() =>
        {
            try
            {
                completion.TrySetResult(executeOnGameplayThread(command));
            }
            catch (Exception exception)
            {
                completion.TrySetResult(UtaRemoteCommandResult.Reject(exception.GetBaseException().Message));
            }
            finally
            {
                registration.Dispose();
            }
        });
        return new ValueTask<UtaRemoteCommandResult>(completion.Task);
    }

    private UtaRemoteCommandResult executeOnGameplayThread(UtaRemoteCommand command)
    {
        switch (command.Name)
        {
            case UtaRemoteCommands.SkipCurrent:
                return skipCurrentToResults();

            case UtaRemoteCommands.Ping:
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.Play:
                gameplayClock.Start();
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.Pause:
                gameplayClock.Stop();
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.TogglePlayback:
                if (gameplayClock.IsPaused.Value)
                    gameplayClock.Start();
                else
                    gameplayClock.Stop();
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.Seek:
                return seek(command.Number!.Value);

            case UtaRemoteCommands.SeekRelative:
                return seek(gameplayClock.CurrentTime + command.Number!.Value);

            case UtaRemoteCommands.Speed:
                audio.PlaybackTempo.Value = Math.Clamp(
                    command.Number!.Value,
                    audio.PlaybackTempo.MinValue,
                    audio.PlaybackTempo.MaxValue);
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.SetLoopA:
                practice.SetLoopPointA();
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.SetLoopB:
                practice.SetLoopPointB();
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.ClearLoop:
                practice.ClearLoopPoints();
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.PreviousPhrase:
                practice.GoToPreviousPhrase();
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.NextPhrase:
                practice.GoToNextPhrase();
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.RetryPhrase:
                practice.RetryPhrase();
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.LoopPhrase:
                practice.LoopCurrentPhrase.Value = command.Enabled!.Value;
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.BackgroundMusicVolume:
                audio.BackgroundMusicVolume.Value = command.Number!.Value;
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.OriginalVocalsVolume:
                audio.OriginalVocalsVolume.Value = (float)command.Number!.Value;
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.MicrophoneMonitorVolume:
                audio.MicrophoneMonitorVolume.Value = (float)command.Number!.Value;
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.Transpose:
                audio.KeyShiftSemitones.Value = (float)Math.Round(command.Number!.Value);
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.OctaveFold:
                modes.OctaveFoldEnabled.Value = command.Enabled!.Value;
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.OriginalVocals:
                {
                    QueueMutationResult result = playback.SetRemoteMod("VOX", command.Enabled!.Value);
                    return result.Succeeded
                        ? UtaRemoteCommandResult.Ok()
                        : UtaRemoteCommandResult.Reject(result.Error ?? "Could not set the VOX mod.");
                }

            case UtaRemoteCommands.MicrophoneLatency:
                audio.MicrophoneLatency.Value = (float)command.Number!.Value;
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.AccompanimentLatency:
                audio.AccompanimentLatency.Value = (float)command.Number!.Value;
                return UtaRemoteCommandResult.Ok();

            case UtaRemoteCommands.LyricsLatency:
                audio.LyricsLatency.Value = (float)command.Number!.Value;
                return UtaRemoteCommandResult.Ok();

            default:
                return UtaRemoteCommandResult.Reject("Unsupported gameplay command.");
        }
    }

    private UtaRemoteCommandResult skipCurrentToResults()
    {
        if (player == null || score == null)
            return UtaRemoteCommandResult.Reject("no_active_gameplay");
        if (remoteSkipInProgress)
            return UtaRemoteCommandResult.Reject("transition_busy");

        if (!player.IsCurrentScreen())
        {
            osu.Framework.Logging.Logger.Log("Uta remote skip ignored: player is not the current screen.");
            return UtaRemoteCommandResult.Ok();
        }

        double trackLength = selectedBeatmap.Value.TrackLoaded ? selectedBeatmap.Value.Track.Length : 0;
        double chartEnd = latestSnapshot.SongLength;
        double seekTarget = new[] { chartEnd, Math.Max(0, trackLength - 50) }.Max();
        osu.Framework.Logging.Logger.Log(
            $"Uta remote skip executing: judged={score.JudgedHits} seekTarget={seekTarget:0}ms trackLength={trackLength:0}ms chartEnd={chartEnd:0}ms paused={gameplayClock.IsPaused.Value}");

        remoteSkipInProgress = true;
        scoringController.RequestForceCompletion();
        score.CompleteRemainingAsMisses();

        if (seekTarget > gameplayClock.CurrentTime + 10)
            seek(seekTarget, Math.Max(chartEnd, trackLength));

        // Player needs one update after the clock seek for its completion state to
        // observe the finalised score. Calling its private transition immediately
        // rewound back to the audio clock, so End song looked accepted but never left.
        gameHost.UpdateThread.Scheduler.AddDelayed(() =>
        {
            try
            {
                if (player.IsCurrentScreen())
                    progress_to_results?.Invoke(player, new object[] { false });
            }
            catch (Exception exception)
            {
                osu.Framework.Logging.Logger.Log($"Uta remote skip results invoke failed: {exception.GetBaseException().Message}");
            }
            finally
            {
                remoteSkipInProgress = false;
            }
        }, 100);

        osu.Framework.Logging.Logger.Log("Uta remote skip accepted.");
        return UtaRemoteCommandResult.Ok();
    }

    private UtaRemoteCommandResult seek(double requestedTime, double? maximumOverride = null)
    {
        double maximum = maximumOverride ?? Math.Max(0, latestSnapshot.SongLength);
        double target = Math.Clamp(requestedTime, 0, maximum > 0 ? maximum : double.MaxValue);

        // UtaGameplaySeeker only permits seeks while running because it was originally written
        // for gap/loop jumps. For remote scrubbing, preserve paused state around the same safe
        // frame-stability workaround.
        bool wasPaused = gameplayClock.IsPaused.Value;
        if (wasPaused)
            gameplayClock.Start();

        bool succeeded = UtaGameplaySeeker.Seek(gameplayClock, target, "remote seek", debugDiagnostics);

        if (wasPaused)
            gameplayClock.Stop();

        return succeeded ? UtaRemoteCommandResult.Ok() : UtaRemoteCommandResult.Reject("Gameplay seek was rejected.");
    }

    private UtaRemoteSnapshot captureSnapshot()
    {
        double time = gameplayClock.CurrentTime;
        int phraseIndex = phraseIndexAt(time);
        string currentLyrics = string.Empty;
        string? nextLyrics = null;
        if (beatmap.Transcript.Count > 0)
        {
            int transcriptIndex = transcriptIndexAt(time);
            currentLyrics = beatmap.Transcript[transcriptIndex].Text;
            if (transcriptIndex + 1 < beatmap.Transcript.Count)
                nextLyrics = beatmap.Transcript[transcriptIndex + 1].Text;
        }

        double speed = audio.PlaybackTempo.Value;
        double? pitch = input.LiveVoiceActive.Value ? input.LiveDetectedPitchMidi.Value : null;
        double totalScore = score?.TotalScore.Value ?? 0;

        return new UtaRemoteSnapshot(
            Interlocked.Increment(ref revision),
            time,
            songLength,
            gameplayClock.IsPaused.Value,
            speed,
            phraseIndex,
            practice.Phrases.Count,
            currentLyrics,
            nextLyrics,
            pitch,
            input.LivePitchSimilarity.Value,
            input.LiveVoiceActive.Value,
            totalScore,
            new UtaRemoteLoopSnapshot(practice.LoopPointA.Value, practice.LoopPointB.Value, practice.LoopCurrentPhrase.Value),
            new UtaRemoteMixerSnapshot(
                audio.BackgroundMusicVolume.Value,
                audio.OriginalVocalsVolume.Value,
                audio.MicrophoneMonitorVolume.Value,
                (int)Math.Round(audio.KeyShiftSemitones.Value),
                modes.OctaveFoldEnabled.Value,
                modes.OriginalVocalsEnabled.Value,
                audio.MicrophoneLatency.Value,
                audio.AccompanimentLatency.Value,
                audio.LyricsLatency.Value),
            Array.Empty<UtaRemoteQueueEntrySnapshot>(),
            false,
            SongTitle: beatmap.BeatmapInfo.Metadata.Title,
            SongArtist: beatmap.BeatmapInfo.Metadata.Artist,
            SongDifficulty: beatmap.BeatmapInfo.DifficultyName,
            SongCreator: beatmap.BeatmapInfo.Metadata.Author.Username);
    }

    private static double calculateSongLength(UtaBeatmap beatmap)
    {
        double length = 0;
        foreach (var hitObject in beatmap.HitObjects)
            length = Math.Max(length, hitObject.EndTime);
        foreach (var segment in beatmap.Transcript)
            length = Math.Max(length, segment.End * 1000);
        return length;
    }

    private int phraseIndexAt(double time)
        => practice.Phrases.Count == 0 ? -1 : UtaPracticeController.PhraseIndexAt(practice.Phrases, time);

    private int transcriptIndexAt(double timeMilliseconds)
    {
        int low = 0;
        int high = beatmap.Transcript.Count;
        double seconds = timeMilliseconds / 1000;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (beatmap.Transcript[middle].Start <= seconds)
                low = middle + 1;
            else
                high = middle;
        }

        return Math.Clamp(low - 1, 0, beatmap.Transcript.Count - 1);
    }

    private static UtaRemoteSnapshot createEmptySnapshot()
        => new(
            0,
            0,
            0,
            true,
            1,
            -1,
            0,
            string.Empty,
            null,
            null,
            0,
            false,
            0,
            new UtaRemoteLoopSnapshot(null, null, false),
            new UtaRemoteMixerSnapshot(1, 0.55, 0.35, 0, false, false, 0, 0, 0),
            Array.Empty<UtaRemoteQueueEntrySnapshot>(),
            false);

    protected override void Dispose(bool isDisposing)
    {
        immersiveAdvanceCancellation.Cancel();
        immersiveAdvanceCancellation.Dispose();
        if (gameplayClock != null)
            gameplayClock.IsPaused.ValueChanged -= onGameplayPausedChanged;
        if (player != null)
            player.OnShowingResults -= onShowingResults;
        // SongSelect.OnResuming calls beginLooping on the *game-wide* beatmap,
        // which may still be the first restarted chart and have no track.
        UtaRulesetRuntime.Instance.PrepareSongSelectPreview();
        lease?.Dispose();
        base.Dispose(isDisposing);
    }
}
