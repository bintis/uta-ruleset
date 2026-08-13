// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Karaoke.Beatmaps;
using osu.Game.Rulesets.Karaoke.Configuration;
using osu.Game.Rulesets.Karaoke.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Karaoke.UI.Uta;

/// <summary>
/// Plays the optional UTZ original mix or guide-vocal stem when the VOX mod is enabled.
/// The vocal track follows the primary track rather than owning gameplay time, so pause, seek
/// and rate changes continue to be controlled by lazer's gameplay clock.
/// </summary>
public partial class UtaGuideVoicePlayer : Component
{
    private readonly BindableDouble backgroundMusicVolume = new();
    private readonly BindableFloat vocalVolume = new();
    private Track? vocalTrack;
    private Track mainTrack = null!;
    private GameplayClockContainer gameplayClock = null!;
    private bool replacesInstrumental;
    private double nextResyncAllowedTime;
    private bool playbackConfirmed;

    [BackgroundDependencyLoader]
    private void load(IBindable<WorkingBeatmap> workingBeatmap, GameplayClockContainer clock, KaraokeBeatmap beatmap, BeatmapManager beatmapManager,
                      KaraokeRulesetConfigManager config, IBindable<IReadOnlyList<Mod>> mods)
    {
        mainTrack = workingBeatmap.Value.Track;
        gameplayClock = clock;
        backgroundMusicVolume.BindTo(config.GetBindable<double>(KaraokeRulesetSetting.BackgroundMusicVolume));
        backgroundMusicVolume.BindValueChanged(value =>
        {
            if (!replacesInstrumental)
                mainTrack.Volume.Value = value.NewValue;
        }, true);

        Logger.Log($"UTZ instrumental ready: {workingBeatmap.Value.BeatmapInfo.Metadata.Title}, " +
                   $"track={mainTrack.Length:0} ms, BGM={backgroundMusicVolume.Value:P0}, local volume={mainTrack.Volume.Value:P0}.");

        if (mods.Value.All(mod => mod is not KaraokeModOriginalVocals))
            return;

        // A separate vocal stem is the only source which can be independently mixed
        // with the instrumental. Fall back to the full original mix when necessary.
        string? audioFile = beatmap.UtaGuideVocalsFile;
        audioFile ??= beatmap.UtaOriginalAudioFile;
        replacesInstrumental = beatmap.UtaGuideVocalsFile == null && !string.IsNullOrWhiteSpace(beatmap.UtaOriginalAudioFile);

        if (string.IsNullOrWhiteSpace(audioFile))
        {
            Logger.Log("Original Vocals is enabled, but this UTZ package has no original or guide-vocal audio.");
            return;
        }

        try
        {
            string? storagePath = workingBeatmap.Value.BeatmapSetInfo.GetPathForFile(audioFile);
            if (storagePath == null)
                return;

            vocalTrack = beatmapManager.BeatmapTrackStore.Get(storagePath);
            if (vocalTrack == null)
            {
                Logger.Log($"UTZ vocal audio was not found: {audioFile}", level: LogLevel.Error);
                return;
            }

            if (replacesInstrumental)
                mainTrack.Volume.Value = 0;

            vocalVolume.BindTo(config.GetBindable<float>(KaraokeRulesetSetting.GuideVoiceVolume));
            vocalVolume.BindValueChanged(value => vocalTrack.Volume.Value = value.NewValue, true);

            vocalTrack.Seek(Math.Max(0, gameplayClock.CurrentTime));
            nextResyncAllowedTime = Time.Current + 1000;
            Logger.Log(replacesInstrumental
                ? $"Loaded UTZ original mix '{audioFile}' ({vocalTrack.Length:0} ms)."
                : $"Loaded UTZ guide voice '{audioFile}' ({vocalTrack.Length:0} ms) at {vocalVolume.Value:P0} volume.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to load UTZ vocal audio '{audioFile}'.");
            restoreInstrumentalVolume();
            vocalTrack?.Dispose();
            vocalTrack = null;
        }
    }

    protected override void Update()
    {
        base.Update();

        if (vocalTrack == null)
            return;

        vocalTrack.Tempo.Value = mainTrack.Tempo.Value;
        vocalTrack.Frequency.Value = mainTrack.Frequency.Value;
        if (replacesInstrumental)
            mainTrack.Volume.Value = 0;
        else
            // Keep the current player's instrumental authoritative. An outgoing
            // player may dispose slightly later during screen transitions and
            // must not leave a shared working track muted.
            mainTrack.Volume.Value = backgroundMusicVolume.Value;

        // WorkingBeatmap.Track is not guaranteed to be the clock source currently
        // owned by the player (notably when lazer decouples the gameplay clock), so
        // its IsRunning state can stay false while the song is audibly playing.
        // Follow lazer's authoritative gameplay clock instead.
        if (gameplayClock.IsRunning && gameplayClock.CurrentTime >= 0)
        {
            if (!vocalTrack.IsRunning)
            {
                // A pause, resume or gameplay seek may have moved the main clock.
                // Align once before resuming instead of repeatedly seeking a live
                // mixer source, which prevents BASS from producing audible output.
                vocalTrack.Seek(gameplayClock.CurrentTime);
                vocalTrack.Start();

                nextResyncAllowedTime = Time.Current + 1000;
                Logger.Log($"Started UTZ vocal audio at {vocalTrack.CurrentTime:0} ms (gameplay {gameplayClock.CurrentTime:0} ms).");
            }
            else if (Time.Current >= nextResyncAllowedTime && Math.Abs(vocalTrack.CurrentTime - gameplayClock.CurrentTime) > 180)
            {
                double drift = vocalTrack.CurrentTime - gameplayClock.CurrentTime;
                vocalTrack.Seek(gameplayClock.CurrentTime);
                nextResyncAllowedTime = Time.Current + 1000;
                Logger.Log($"Resynchronised UTZ vocal audio after {drift:+0;-0;0} ms drift.");
            }

            if (!playbackConfirmed && vocalTrack.CurrentTime > gameplayClock.CurrentTime - 500)
            {
                playbackConfirmed = true;
                Logger.Log($"UTZ vocal playback active: running={vocalTrack.IsRunning}, time={vocalTrack.CurrentTime:0} ms, volume={vocalTrack.Volume.Value:P0}.");
            }
        }
        else if (vocalTrack.IsRunning)
        {
            vocalTrack.Stop();
            playbackConfirmed = false;
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        vocalTrack?.Stop();
        vocalTrack?.Dispose();
        backgroundMusicVolume.UnbindAll();
        vocalVolume.UnbindAll();
        base.Dispose(isDisposing);
    }

    private void restoreInstrumentalVolume()
    {
        if (replacesInstrumental && mainTrack != null)
            mainTrack.Volume.Value = backgroundMusicVolume.Value;

        replacesInstrumental = false;
    }
}
