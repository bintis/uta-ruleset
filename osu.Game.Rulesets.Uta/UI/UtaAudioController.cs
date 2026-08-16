// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Keeps independently routed BGM and vocal sources aligned to lazer's
/// authoritative gameplay clock.
/// </summary>
internal sealed partial class UtaAudioController : Component
{
    private readonly BindableDouble backgroundMusicVolume = new();
    private readonly BindableFloat vocalsVolume = new();
    private readonly Bindable<string> backgroundMusicOutput = new();
    private readonly Bindable<string> vocalsOutput = new();
    private readonly BindableDouble mainTrackVolumeAdjustment = new(1);
    private readonly BindableFloat keyShiftSemitones = new();
    private readonly BindableFloat accompanimentLatency = new();
    private readonly BindableBool debugDiagnostics = new();
    private IBindable<bool> gameplayPaused = null!;
    private long diagnosticIntervalStart;

    private Track mainTrack = null!;
    private GameplayClockContainer gameplayClock = null!;
    private UtaRoutedAudioStream? backgroundMusic;
    private UtaRoutedAudioStream? vocals;
    private bool accompanimentLatencyResyncPending;
    private long accompanimentLatencyChangedAt;

    [BackgroundDependencyLoader]
    private void load(AudioManager audioManager, UtaAudioRouter router, IBindable<WorkingBeatmap> workingBeatmap,
                      GameplayClockContainer gameplayClock, UtaBeatmap beatmap,
                      UtaAudioSettingsState audioSettings, IBindable<IReadOnlyList<Mod>> mods)
    {
        this.gameplayClock = gameplayClock;
        router.Initialise(audioManager);
        WorkingBeatmap working = workingBeatmap.Value;
        mainTrack = working.Track;

        backgroundMusicVolume.BindTo(audioSettings.BackgroundMusicVolume);
        backgroundMusicOutput.BindTo(audioSettings.BackgroundMusicOutputDevice);
        vocalsVolume.BindTo(audioSettings.OriginalVocalsVolume);
        vocalsOutput.BindTo(audioSettings.OriginalVocalsOutputDevice);
        keyShiftSemitones.BindTo(audioSettings.KeyShiftSemitones);
        accompanimentLatency.BindTo(audioSettings.AccompanimentLatency);
        debugDiagnostics.BindTo(audioSettings.DebugDiagnostics);

        backgroundMusic = tryCreateTrack(router, working, working.Metadata.AudioFile, backgroundMusicOutput.Value, "BGM");

        // Original vocals are opt-in. Do not create a vocal bus unless VOX is selected.
        bool originalVocalsEnabled = mods.Value.Any(mod => mod is UtaModOriginalVocals);
        string? vocalFile = originalVocalsEnabled
            ? beatmap.OriginalAudioFile ?? beatmap.GuideVocalsFile
            : null;

        if (string.IsNullOrWhiteSpace(vocalFile))
            Logger.Log("This UTZ package has no guide-vocal or original audio track; the vocals control is unavailable.");
        else
            vocals = tryCreateTrack(router, working, vocalFile, vocalsOutput.Value, "vocals");

        // The working beatmap track is reused by song select for previews. A
        // temporary adjustment avoids leaking a zero base volume after gameplay.
        mainTrackVolumeAdjustment.Value = backgroundMusic != null ? 0 : backgroundMusicVolume.Value;
        mainTrack.AddAdjustment(AdjustableProperty.Volume, mainTrackVolumeAdjustment);

        backgroundMusicVolume.BindValueChanged(value =>
        {
            backgroundMusic?.SetVolume((float)value.NewValue);
            if (backgroundMusic == null)
                mainTrackVolumeAdjustment.Value = value.NewValue;
        }, true);
        vocalsVolume.BindValueChanged(value =>
        {
            if (vocals == null)
                return;

            vocals.SetVolume(value.NewValue);
            Logger.Log($"Uta vocals volume applied: {value.NewValue:P0}");
        }, true);
        backgroundMusicOutput.BindValueChanged(value => backgroundMusic?.SetOutput(value.NewValue));
        vocalsOutput.BindValueChanged(value => vocals?.SetOutput(value.NewValue));
        keyShiftSemitones.BindValueChanged(value =>
        {
            int semitones = (int)MathF.Round(value.NewValue);
            backgroundMusic?.SetPitch(semitones);
            vocals?.SetPitch(semitones);
        }, true);
        accompanimentLatency.BindValueChanged(_ =>
        {
            accompanimentLatencyChangedAt = Stopwatch.GetTimestamp();
            accompanimentLatencyResyncPending = true;
        });
        gameplayPaused = gameplayClock.IsPaused;
        gameplayPaused.BindValueChanged(onGameplayPausedChanged, true);
        gameplayClock.OnSeek += synchroniseAfterSeek;
    }

    private void onGameplayPausedChanged(ValueChangedEvent<bool> paused)
    {
        if (paused.NewValue)
        {
            backgroundMusic?.Stop();
            vocals?.Stop();
        }
        else
            synchroniseAfterSeek();
    }

    protected override void Update()
    {
        base.Update();
        if (accompanimentLatencyResyncPending
            && Stopwatch.GetElapsedTime(accompanimentLatencyChangedAt).TotalMilliseconds >= 100)
        {
            accompanimentLatencyResyncPending = false;
            synchroniseAfterSeek();
        }

        if (backgroundMusic == null && vocals == null)
            return;

        double current = gameplayClock.CurrentTime;
        double sourceTime = current - accompanimentLatency.Value;
        bool shouldRun = !gameplayPaused.Value && gameplayClock.IsRunning && sourceTime >= 0;
        double rate = gameplayClock.Rate;
        updateSource(backgroundMusic, sourceTime, rate, shouldRun);
        updateSource(vocals, sourceTime, rate, shouldRun);

        if (debugDiagnostics.Value)
            logDiagnostics(rate, sourceTime, shouldRun);
    }

    private void logDiagnostics(double rate, double sourceTime, bool running)
    {
        if (Stopwatch.GetElapsedTime(diagnosticIntervalStart).TotalMilliseconds < 5000)
            return;

        diagnosticIntervalStart = Stopwatch.GetTimestamp();
        if (!running)
            return;

        string bgmDrift = describeDrift(backgroundMusic, sourceTime);
        string voxDrift = describeDrift(vocals, sourceTime);
        Logger.Log($"Uta debug audio: rate={rate:0.000} expected={sourceTime:0.0}ms bgm={bgmDrift} vocals={voxDrift}");
    }

    private static string describeDrift(UtaRoutedAudioStream? source, double expected)
    {
        if (source == null)
            return "n/a";

        double actual = source.GetPositionMs();
        return $"{actual:0.0}ms (drift {actual - expected:+0.0;-0.0;0.0}ms)";
    }

    private static void updateSource(UtaRoutedAudioStream? source, double current, double rate, bool shouldRun)
    {
        if (source == null)
            return;

        source.SetRate(rate);
        if (shouldRun)
        {
            if (!source.IsRunning)
            {
                source.Seek(current);
                source.Start();
            }
        }
        else if (source.IsRunning)
        {
            source.Stop();
        }
    }

    private void synchroniseAfterSeek()
    {
        synchroniseSource(backgroundMusic);
        synchroniseSource(vocals);
    }

    private void synchroniseSource(UtaRoutedAudioStream? source)
    {
        if (source == null)
            return;

        source.Stop();
        source.Seek(gameplayClock.CurrentTime - accompanimentLatency.Value);
    }

    private static UtaRoutedAudioStream? createTrack(UtaAudioRouter router, WorkingBeatmap working, string? file, string output)
    {
        if (string.IsNullOrWhiteSpace(file))
            return null;

        string? storagePath = working.BeatmapSetInfo.GetPathForFile(file);
        if (storagePath == null)
            return null;

        using Stream input = working.GetStream(storagePath);
        if (input == null)
            return null;

        using var memory = new MemoryStream();
        input.CopyTo(memory);
        return router.CreateTrack(memory.ToArray(), output);
    }

    private static UtaRoutedAudioStream? tryCreateTrack(UtaAudioRouter router, WorkingBeatmap working, string? file, string output, string routeName)
    {
        try
        {
            return createTrack(router, working, file, output);
        }
        catch (Exception ex)
        {
            Logger.Log($"Uta {routeName} output routing is unavailable for '{file}': {ex.Message}. Falling back to lazer audio.");
            return null;
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        backgroundMusic?.Dispose();
        vocals?.Dispose();
        if (mainTrack != null)
            mainTrack.RemoveAdjustment(AdjustableProperty.Volume, mainTrackVolumeAdjustment);
        if (gameplayClock != null)
            gameplayClock.OnSeek -= synchroniseAfterSeek;
        backgroundMusicVolume.UnbindAll();
        vocalsVolume.UnbindAll();
        backgroundMusicOutput.UnbindAll();
        vocalsOutput.UnbindAll();
        keyShiftSemitones.UnbindAll();
        accompanimentLatency.UnbindAll();
        debugDiagnostics.UnbindAll();
        if (gameplayPaused != null)
            gameplayPaused.ValueChanged -= onGameplayPausedChanged;
        base.Dispose(isDisposing);
    }
}
