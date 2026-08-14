// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
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
    private IBindable<bool> gameplayPaused = null!;

    private Track mainTrack = null!;
    private GameplayClockContainer gameplayClock = null!;
    private UtaRoutedAudioStream? backgroundMusic;
    private UtaRoutedAudioStream? vocals;
    private double nextResyncAllowedTime;

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

        backgroundMusic = tryCreateTrack(router, working, working.Metadata.AudioFile, backgroundMusicOutput.Value, "BGM");

        // Vocal and instrumental playback are separate buses. VOX only chooses
        // the full original mix over an available isolated guide-vocal stem; it
        // must not decide whether the vocal bus exists at all.
        bool preferOriginalMix = mods.Value.Any(mod => mod is UtaModOriginalVocals)
                                 && !string.IsNullOrWhiteSpace(beatmap.OriginalAudioFile);
        string? vocalFile = preferOriginalMix
            ? beatmap.OriginalAudioFile
            : beatmap.GuideVocalsFile ?? beatmap.OriginalAudioFile;

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
        {
            synchroniseAfterSeek();
            nextResyncAllowedTime = 0;
        }
    }

    protected override void Update()
    {
        base.Update();
        if (backgroundMusic == null && vocals == null)
            return;

        updateSource(backgroundMusic);
        updateSource(vocals);
    }

    private void updateSource(UtaRoutedAudioStream? source)
    {
        if (source == null)
            return;

        source.SetRate(mainTrack.Rate);
        if (!gameplayPaused.Value && gameplayClock.IsRunning && gameplayClock.CurrentTime >= 0)
        {
            if (!source.IsRunning)
            {
                source.Seek(gameplayClock.CurrentTime);
                source.Start();
                nextResyncAllowedTime = Time.Current + 1000;
            }
            else if (Time.Current >= nextResyncAllowedTime && Math.Abs(source.CurrentTime - gameplayClock.CurrentTime) > 180)
            {
                source.Seek(gameplayClock.CurrentTime);
                nextResyncAllowedTime = Time.Current + 1000;
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
        nextResyncAllowedTime = 0;
    }

    private void synchroniseSource(UtaRoutedAudioStream? source)
    {
        if (source == null)
            return;

        source.Stop();
        source.Seek(gameplayClock.CurrentTime);
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
        if (gameplayPaused != null)
            gameplayPaused.ValueChanged -= onGameplayPausedChanged;
        base.Dispose(isDisposing);
    }
}
