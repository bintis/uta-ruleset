// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Remote;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Gameplay audio: osu <see cref="Track"/> is the clock and default-device BGM.
/// Extra BASS routes exist only for another output device or accompaniment delay.
/// Vocals on the default device are a second osu <see cref="Track"/> slaved to the same clock.
/// </summary>
internal sealed partial class UtaAudioController : Component
{
    private readonly BindableDouble backgroundMusicVolume = new();
    private readonly BindableFloat vocalsVolume = new();
    private readonly Bindable<string> backgroundMusicOutput = new();
    private readonly Bindable<string> vocalsOutput = new();
    private readonly BindableDouble mainTrackVolumeAdjustment = new(1);
    private readonly BindableDouble playbackTempo = new(1);
    private readonly BindableDouble runtimeModFrequency = new(1);
    private readonly BindableDouble transposeFrequency = new(1);
    private readonly BindableDouble transposeTempo = new(1);
    private readonly BindableDouble vocalsEffectiveVolume = new();
    private readonly BindableFloat keyShiftSemitones = new();
    private readonly BindableBool originalVocalsEnabled = new();
    private readonly BindableFloat accompanimentLatency = new();
    private readonly BindableBool debugDiagnostics = new();
    private IBindable<bool> gameplayPaused = null!;
    private long diagnosticIntervalStart;

    private Track mainTrack = null!;
    private Track? vocalsTrack;
    private ITrackStore? vocalStore;
    private GameplayClockContainer gameplayClock = null!;
    private AudioManager audioManager = null!;
    private UtaAudioRouter router = null!;
    private WorkingBeatmap working = null!;
    private UtaBeatmap beatmap = null!;
    private UtaRoutedAudioStream? backgroundMusic;
    private UtaRoutedAudioStream? vocals;
    private bool accompanimentLatencyResyncPending;
    private long accompanimentLatencyChangedAt;
    private bool adjustmentsApplied;
    private bool halted;
    private static readonly List<UtaAudioController> live_controllers = new();

    internal bool HasActiveVocals => vocals != null || vocalsTrack != null;
    internal double VocalsPosition => vocals?.GetPositionMs() ?? vocalsTrack?.CurrentTime ?? 0;

    [Resolved(canBeNull: true)]
    private MusicController? music { get; set; }

    internal static void HaltAllPlayback()
    {
        UtaAudioRouter.HaltAllPlayback();
        UtaAudioController[] snapshot;
        lock (live_controllers)
            snapshot = live_controllers.ToArray();
        foreach (UtaAudioController controller in snapshot)
            controller.halt();
    }

    internal static void DestroyAllPlayback()
    {
        UtaAudioRouter.DestroyBuses();
        UtaAudioController[] snapshot;
        lock (live_controllers)
            snapshot = live_controllers.ToArray();
        foreach (UtaAudioController controller in snapshot)
            controller.halt();
    }

    internal static void HaltIfSingleSession()
    {
        lock (live_controllers)
        {
            if (live_controllers.Count != 1)
                return;
        }

        HaltAllPlayback();
    }

    /// <summary>
    /// First play after process start is clean. Later plays must match that:
    /// destroy Uta mixers, stop MusicController if it is still playing, and
    /// stop the previous chart's track only when it is loaded and not the
    /// incoming clock track; creating another native track here reintroduces duplicate playback ownership.
    /// </summary>
    private void beginFreshSession(WorkingBeatmap incoming)
    {
        DestroyAllPlayback();
        stopPreviousChartTrack(incoming);
        if (music == null)
        {
            Logger.Log("Uta audio session reset: MusicController missing");
            return;
        }

        try
        {
            music.ResetTrackAdjustments();
            bool wasPlaying = music.IsPlaying;
            if (music.CurrentTrack.IsRunning)
                music.CurrentTrack.Stop();
            else if (wasPlaying)
                music.Stop(requestedByUser: false);
            Logger.Log($"Uta audio session reset: musicWasPlaying={wasPlaying} stillPlaying={music.IsPlaying} trackLoaded={music.TrackLoaded}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Uta audio session reset failed: {ex.Message}", level: LogLevel.Error);
        }
    }

    private static void stopPreviousChartTrack(WorkingBeatmap incoming)
    {
        WorkingBeatmap? previous = UtaRulesetRuntime.Instance.LastPlayedBeatmap;
        if (previous == null || previous.BeatmapInfo.ID == incoming.BeatmapInfo.ID)
            return;

        if (!previous.TrackLoaded)
        {
            Logger.Log($"Uta audio session reset: previous '{previous}' track not loaded");
            return;
        }

        try
        {
            bool running = previous.Track.IsRunning;
            if (running)
                previous.Track.Stop();
            bool still = previous.Track.IsRunning;
            Logger.Log($"Uta audio session reset: previous='{previous}' running={running} stillRunning={still} current='{incoming}'");
        }
        catch (Exception ex)
        {
            Logger.Log($"Uta audio session reset previous track: {ex.Message}", level: LogLevel.Error);
        }
    }

    [BackgroundDependencyLoader]
    private void load(AudioManager audioManager, UtaAudioRouter router, IBindable<WorkingBeatmap> workingBeatmap,
                      GameplayClockContainer gameplayClock, UtaBeatmap beatmap,
                      UtaAudioSettingsState audioSettings, UtaRuntimeModeState runtimeModes)
    {
        this.gameplayClock = gameplayClock;
        this.audioManager = audioManager;
        this.router = router;
        this.beatmap = beatmap;
        working = workingBeatmap.Value;
        if (music != null)
            UtaRulesetRuntime.Instance.AttachMusicController(music);
        beginFreshSession(working);
        mainTrack = working.Track;
        router.Initialise(audioManager);
        lock (live_controllers)
            live_controllers.Add(this);

        backgroundMusicVolume.BindTo(audioSettings.BackgroundMusicVolume);
        backgroundMusicOutput.BindTo(audioSettings.BackgroundMusicOutputDevice);
        vocalsVolume.BindTo(audioSettings.OriginalVocalsVolume);
        vocalsOutput.BindTo(audioSettings.OriginalVocalsOutputDevice);
        playbackTempo.BindTo(audioSettings.PlaybackTempo);
        runtimeModFrequency.BindTo(audioSettings.RuntimeModFrequency);
        keyShiftSemitones.BindTo(audioSettings.KeyShiftSemitones);
        originalVocalsEnabled.BindTo(runtimeModes.OriginalVocalsEnabled);
        accompanimentLatency.BindTo(audioSettings.AccompanimentLatency);
        debugDiagnostics.BindTo(audioSettings.DebugDiagnostics);
        diagnosticIntervalStart = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 15 / 4;

        mainTrack.AddAdjustment(AdjustableProperty.Volume, mainTrackVolumeAdjustment);
        mainTrack.AddAdjustment(AdjustableProperty.Tempo, playbackTempo);
        mainTrack.AddAdjustment(AdjustableProperty.Frequency, runtimeModFrequency);
        mainTrack.AddAdjustment(AdjustableProperty.Frequency, transposeFrequency);
        mainTrack.AddAdjustment(AdjustableProperty.Tempo, transposeTempo);
        adjustmentsApplied = true;

        keyShiftSemitones.BindValueChanged(value =>
        {
            (transposeFrequency.Value, transposeTempo.Value) = UtaAudioMath.TransposeFactors((int)MathF.Round(value.NewValue));
        }, true);

        gameplayPaused = gameplayClock.IsPaused;
        gameplayPaused.BindValueChanged(onGameplayPausedChanged);
        gameplayClock.OnSeek += synchroniseAfterSeek;
        mainTrack.AggregateFrequency.BindValueChanged(onTrackRateChanged);
        mainTrack.AggregateTempo.BindValueChanged(onTrackRateChanged);

        if (debugDiagnostics.Value)
        {
            Logger.Log(
                $"Uta audio graph: chart='{working}' id={working.BeatmapInfo.ID} "
                + $"playableId={beatmap.BeatmapInfo.ID} bgmFile='{working.Metadata.AudioFile}' "
                + $"vocalFile='{beatmap.OriginalAudioFile ?? beatmap.GuideVocalsFile}' "
                + $"bgmOutput='{backgroundMusicOutput.Value}' vocalsOutput='{vocalsOutput.Value}' "
                + $"latency={accompanimentLatency.Value:0}ms");
        }

        rebuildBackground();
        rebuildVocals();

        backgroundMusicVolume.BindValueChanged(_ => applyBackgroundVolume(), true);
        backgroundMusicOutput.BindValueChanged(_ => rebuildBackground());
        vocalsVolume.BindValueChanged(_ => applyVocalsVolume(), true);
        originalVocalsEnabled.BindValueChanged(_ => rebuildVocals(), true);
        vocalsOutput.BindValueChanged(_ => rebuildVocals());
        accompanimentLatency.BindValueChanged(_ =>
        {
            accompanimentLatencyChangedAt = Stopwatch.GetTimestamp();
            accompanimentLatencyResyncPending = true;
        });
    }

    private void onTrackRateChanged(ValueChangedEvent<double> _) => applySlaveRates();

    private void rebuildBackground()
    {
        if (halted)
            return;

        bool route = UtaAudioMath.NeedsRoutedBgm(
            UtaAudioMath.NeedsRoutedOutput(backgroundMusicOutput.Value, router.DefaultDevice),
            accompanimentLatency.Value);

        if (route)
        {
            backgroundMusic ??= tryCreateRouted(working.Metadata.AudioFile, backgroundMusicOutput.Value, "BGM");
            backgroundMusic?.SetOutput(backgroundMusicOutput.Value);
            applySlaveRates();
        }
        else
        {
            backgroundMusic?.Dispose();
            backgroundMusic = null;
        }

        applyBackgroundVolume();
        synchroniseAfterSeek();
    }

    private void rebuildVocals()
    {
        if (halted)
            return;

        if (!originalVocalsEnabled.Value)
        {
            if (debugDiagnostics.Value)
                Logger.Log("Uta vocals skipped: originalVocalsEnabled=False (need VOX or the persisted original-vocals preference)");
            disposeVocals();
            applyVocalsVolume();
            return;
        }

        string? vocalFile = beatmap.OriginalAudioFile ?? beatmap.GuideVocalsFile;
        if (string.IsNullOrWhiteSpace(vocalFile))
        {
            Logger.Log("This UTZ package has no guide-vocal or original audio track; the vocals control is unavailable.");
            disposeVocals();
            return;
        }

        // Native VOX is a second osu TrackBass. Routed BGM is a different mixer
        // on the same speaker. After leftover halt, CurrentDevice can sit on
        // that mixer and the native vocal Track is silent (AUDIO leftover doc §24).
        bool customOutput = UtaAudioMath.NeedsRoutedOutput(vocalsOutput.Value, router.DefaultDevice);
        bool bgmRouted = backgroundMusic != null
                         || UtaAudioMath.NeedsRoutedBgm(
                             UtaAudioMath.NeedsRoutedOutput(backgroundMusicOutput.Value, router.DefaultDevice),
                             accompanimentLatency.Value);
        bool route = UtaAudioMath.NeedsRoutedVocals(customOutput, bgmRouted);
        disposeVocals();

        if (route)
        {
            vocals = tryCreateRouted(vocalFile, vocalsOutput.Value, "vocals");
            applySlaveRates();
        }
        else
        {
            vocalsTrack = tryCreateNativeTrack(vocalFile);
            if (vocalsTrack != null)
            {
                vocalsTrack.BindAdjustments(gameplayClock.AdjustmentsFromMods);
                vocalsTrack.AddAdjustment(AdjustableProperty.Volume, vocalsEffectiveVolume);
                vocalsTrack.AddAdjustment(AdjustableProperty.Tempo, playbackTempo);
                vocalsTrack.AddAdjustment(AdjustableProperty.Frequency, runtimeModFrequency);
                vocalsTrack.AddAdjustment(AdjustableProperty.Frequency, transposeFrequency);
                vocalsTrack.AddAdjustment(AdjustableProperty.Tempo, transposeTempo);
                if (gameplayClock is MasterGameplayClockContainer master)
                    vocalsTrack.AddAdjustment(AdjustableProperty.Frequency, master.UserPlaybackRate);
            }
            else
            {
                vocals = tryCreateRouted(vocalFile, vocalsOutput.Value, "vocals");
                applySlaveRates();
            }
        }

        applyVocalsVolume();
        synchroniseAfterSeek();
        if (debugDiagnostics.Value)
            Logger.Log($"Uta vocals route ready: file='{vocalFile}' routed={route} native={vocalsTrack != null}");
    }

    private void applyBackgroundVolume()
    {
        if (halted)
            return;

        if (backgroundMusic != null)
        {
            mainTrackVolumeAdjustment.Value = 0;
            backgroundMusic.SetVolume((float)backgroundMusicVolume.Value);
        }
        else
            mainTrackVolumeAdjustment.Value = backgroundMusicVolume.Value;
    }

    private void applyVocalsVolume()
    {
        if (halted)
            return;

        float effective = UtaAudioMath.EffectiveVocalsVolume(originalVocalsEnabled.Value, vocalsVolume.Value);
        vocalsEffectiveVolume.Value = effective;
        vocals?.SetVolume(effective);

        if (debugDiagnostics.Value)
            Logger.Log($"Uta vocals volume applied: {effective:P0} (enabled={originalVocalsEnabled.Value} slider={vocalsVolume.Value:P0})");
    }

    private void applySlaveRates()
    {
        if (halted)
            return;

        double frequency = mainTrack.AggregateFrequency.Value;
        double tempo = mainTrack.AggregateTempo.Value;
        backgroundMusic?.SetFrequency(frequency);
        backgroundMusic?.SetTempo(tempo);
        vocals?.SetFrequency(frequency);
        vocals?.SetTempo(tempo);
    }

    private void onGameplayPausedChanged(ValueChangedEvent<bool> paused)
    {
        if (halted)
            return;

        if (paused.NewValue)
        {
            // Clock-stop is the last update-thread moment before Player.Exit
            // schedules async dispose. Free routed BGM here or MixerNonStop
            // keeps the previous chart playing into the next one.
            dropRoutedPlayback();
            disposeVocals();
            // PlayerLoader.OnSuspending: stop the track before removing volume
            // adjustment to avoid a spike. SongSelect.ensurePlayingSelected will
            // resume MusicController unless we Stop CurrentTrack first and set
            // UserPauseRequested (AUDIO leftover doc §21).
            if (mainTrack.IsRunning)
                mainTrack.Stop();
            UtaRulesetRuntime.Instance.SilenceMusicController();
        }
        else
        {
            rebuildBackground();
            rebuildVocals();
        }
    }

    private void dropRoutedPlayback()
    {
        stopSlaves();
        UtaAudioRouter.HaltAllPlayback();
        backgroundMusic?.Dispose();
        backgroundMusic = null;
        vocals?.Dispose();
        vocals = null;
    }

    protected override void Update()
    {
        base.Update();
        if (halted)
            return;

        if (accompanimentLatencyResyncPending
            && Stopwatch.GetElapsedTime(accompanimentLatencyChangedAt).TotalMilliseconds >= 100)
        {
            accompanimentLatencyResyncPending = false;
            rebuildBackground();
        }

        double expected = sourceTime();
        bool shouldRun = slavesShouldRun();
        followClock(backgroundMusic, expected, shouldRun);
        followClock(vocals, expected, shouldRun && originalVocalsEnabled.Value);
        followClock(vocalsTrack, expected, shouldRun && originalVocalsEnabled.Value);

        if (debugDiagnostics.Value)
            logDiagnostics(expected, shouldRun);
    }

    private static void followClock(UtaRoutedAudioStream? source, double expected, bool shouldRun)
    {
        if (source == null || source.Handle == 0)
            return;

        if (!shouldRun)
        {
            if (source.IsRunning)
                source.Stop();
            return;
        }

        if (!source.IsRunning || UtaAudioMath.DriftNeedsCorrection(expected, source.GetPositionMs()))
        {
            source.Stop();
            source.Seek(expected);
            source.Start();
        }
    }

    private static void followClock(Track? track, double expected, bool shouldRun)
    {
        if (track == null)
            return;

        if (!shouldRun)
        {
            if (track.IsRunning)
                track.Stop();
            return;
        }

        if (!track.IsRunning || UtaAudioMath.DriftNeedsCorrection(expected, track.CurrentTime))
        {
            if (track.IsRunning)
                track.Stop();
            track.Seek(Math.Max(0, expected));
            track.Start();
        }
    }

    private void logDiagnostics(double sourceTime, bool running)
    {
        if (Stopwatch.GetElapsedTime(diagnosticIntervalStart).TotalMilliseconds < 5000)
            return;

        diagnosticIntervalStart = Stopwatch.GetTimestamp();
        if (!running)
            return;

        Logger.Log(
            $"Uta debug audio: clock=trackbass rate={gameplayClock.Rate:0.000} freq={mainTrack.AggregateFrequency.Value:0.000} "
            + $"tempo={mainTrack.AggregateTempo.Value:0.000} expected={sourceTime:0.0}ms "
            + $"bgm={(backgroundMusic == null ? "master" : describeDrift(backgroundMusic, sourceTime))} "
            + $"vocals={(vocals != null ? describeDrift(vocals, sourceTime) : vocalsTrack != null ? $"{vocalsTrack.CurrentTime:0.0}ms" : "n/a")}");
    }

    private static string describeDrift(UtaRoutedAudioStream source, double expected)
    {
        double actual = source.GetPositionMs();
        return $"{actual:0.0}ms (drift {actual - expected:+0.0;-0.0;0.0}ms)";
    }

    private double sourceTime()
        => gameplayClock.CurrentTime - (backgroundMusic != null ? accompanimentLatency.Value : 0);

    private bool slavesShouldRun()
        => gameplayPaused is { Value: false } && gameplayClock.IsRunning && sourceTime() >= 0;

    private void synchroniseAfterSeek()
    {
        if (halted)
            return;

        double time = sourceTime();
        bool run = slavesShouldRun();
        syncRouted(backgroundMusic, time, run);
        syncRouted(vocals, time, run && originalVocalsEnabled.Value);
        syncTrack(vocalsTrack, time, run && originalVocalsEnabled.Value);
        applyBackgroundVolume();
    }

    private static void syncRouted(UtaRoutedAudioStream? source, double time, bool run)
    {
        if (source == null || source.Handle == 0)
            return;

        source.Stop();
        source.Seek(time);
        if (run)
            source.Start();
    }

    private static void syncTrack(Track? track, double time, bool run)
    {
        if (track == null)
            return;

        if (track.IsRunning)
            track.Stop();
        track.Seek(Math.Max(0, time));
        if (run)
            track.Start();
    }

    private void stopSlaves()
    {
        backgroundMusic?.Stop();
        vocals?.Stop();
        if (vocalsTrack?.IsRunning == true)
            vocalsTrack.Stop();
    }

    private Track? tryCreateNativeTrack(string file)
    {
        string? storagePath = working.BeatmapSetInfo.GetPathForFile(file);
        if (storagePath == null)
            return null;

        int previous = router.CaptureDevice();
        try
        {
            router.UseDefaultDevice();
            vocalStore ??= audioManager.GetTrackStore(new BeatmapFileStore(working));
            return vocalStore.Get(storagePath);
        }
        catch (Exception ex)
        {
            Logger.Log($"Uta vocals track unavailable for '{file}': {ex.Message}. Falling back to routed audio.");
            return null;
        }
        finally
        {
            router.RestoreDevice(previous);
        }
    }

    private UtaRoutedAudioStream? tryCreateRouted(string? file, string output, string routeName)
    {
        if (string.IsNullOrWhiteSpace(file))
            return null;

        string? storagePath = working.BeatmapSetInfo.GetPathForFile(file);
        if (storagePath == null)
            return null;

        try
        {
            using Stream input = working.GetStream(storagePath);
            if (input == null)
                return null;

            using var memory = new MemoryStream();
            input.CopyTo(memory);
            return router.CreateTrack(memory.ToArray(), output);
        }
        catch (Exception ex)
        {
            Logger.Log($"Uta {routeName} output routing is unavailable for '{file}': {ex.Message}.");
            return null;
        }
    }

    private void disposeVocals()
    {
        vocals?.Stop();
        vocals?.Dispose();
        vocals = null;
        if (vocalsTrack != null)
        {
            if (vocalsTrack.IsRunning)
                vocalsTrack.Stop();
            vocalsTrack.UnbindAdjustments(gameplayClock.AdjustmentsFromMods);
            vocalsTrack.Dispose();
            vocalsTrack = null;
        }
    }

    private void halt()
    {
        if (halted)
            return;

        halted = true;
        stopSlaves();
        backgroundMusic?.Dispose();
        backgroundMusic = null;
        disposeVocals();
        if (adjustmentsApplied && mainTrack != null)
        {
            if (mainTrack.IsRunning)
                mainTrack.Stop();
            mainTrackVolumeAdjustment.Value = 0;
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        lock (live_controllers)
            live_controllers.Remove(this);
        halt();
        if (gameplayClock != null)
            gameplayClock.OnSeek -= synchroniseAfterSeek;
        if (mainTrack != null)
        {
            mainTrack.AggregateFrequency.ValueChanged -= onTrackRateChanged;
            mainTrack.AggregateTempo.ValueChanged -= onTrackRateChanged;
        }
        stopSlaves();
        backgroundMusic?.Dispose();
        backgroundMusic = null;
        disposeVocals();
        vocalStore?.Dispose();
        if (adjustmentsApplied && mainTrack != null)
        {
            mainTrackVolumeAdjustment.Value = 0;
            if (mainTrack.IsRunning)
                mainTrack.Stop();
            mainTrack.RemoveAdjustment(AdjustableProperty.Volume, mainTrackVolumeAdjustment);
            mainTrack.RemoveAdjustment(AdjustableProperty.Tempo, playbackTempo);
            mainTrack.RemoveAdjustment(AdjustableProperty.Frequency, runtimeModFrequency);
            mainTrack.RemoveAdjustment(AdjustableProperty.Frequency, transposeFrequency);
            mainTrack.RemoveAdjustment(AdjustableProperty.Tempo, transposeTempo);
        }

        backgroundMusicVolume.UnbindAll();
        vocalsVolume.UnbindAll();
        backgroundMusicOutput.UnbindAll();
        vocalsOutput.UnbindAll();
        playbackTempo.UnbindAll();
        runtimeModFrequency.UnbindAll();
        keyShiftSemitones.UnbindAll();
        originalVocalsEnabled.UnbindAll();
        accompanimentLatency.UnbindAll();
        debugDiagnostics.UnbindAll();
        if (gameplayPaused != null)
            gameplayPaused.ValueChanged -= onGameplayPausedChanged;

        // PlayerLoader can be cancelled after this component has loaded but before
        // UtaGameplaySessionBridge reaches LoadComplete. In that path the normal
        // screen-leave hook never runs, while halt() has already stopped the shared
        // WorkingBeatmap track. Repair the exact SongSelect bindable before it resumes
        // or PrepareTrackForPreview/PlayerLoader will access an unloaded Track.
        UtaRulesetRuntime.Instance.StopLeftoverOnLeave();
        base.Dispose(isDisposing);
    }

    private sealed class BeatmapFileStore : IResourceStore<byte[]>
    {
        private readonly WorkingBeatmap working;

        public BeatmapFileStore(WorkingBeatmap working)
        {
            this.working = working;
        }

        public void Dispose()
        {
        }

        public byte[] Get(string name)
        {
            using Stream? stream = GetStream(name);
            if (stream == null)
                return Array.Empty<byte>();

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(Get(name));

        public Stream? GetStream(string name) => working.GetStream(name);

        public IEnumerable<string> GetAvailableResources() => Array.Empty<string>();
    }
}
