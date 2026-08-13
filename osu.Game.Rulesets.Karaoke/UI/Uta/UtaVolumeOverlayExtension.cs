// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Overlays.Volume;
using osu.Game.Rulesets.Karaoke.Configuration;

namespace osu.Game.Rulesets.Karaoke.UI.Uta;

/// <summary>
/// Temporarily repurposes lazer's three native volume meters for karaoke. This
/// preserves the original scroll selection, dragging, animations and layout.
/// </summary>
public partial class UtaVolumeOverlayExtension : Component
{
    private readonly BindableDouble vocalVolume = new() { MinValue = 0, MaxValue = 1, Precision = 0.01 };
    private readonly BindableDouble microphoneVolume = new() { MinValue = 0, MaxValue = 1, Precision = 0.01 };
    private readonly BindableFloat vocalConfig = new();
    private readonly BindableFloat microphoneConfig = new();
    private readonly BindableDouble backgroundMusicConfig = new();

    private VolumeOverlay? volumeOverlay;
    private AudioManager? audioManager;
    private VolumeMeter? effectMeter;
    private VolumeMeter? masterMeter;
    private VolumeMeter? musicMeter;
    private OsuSpriteText? effectLabel;
    private OsuSpriteText? masterLabel;
    private OsuSpriteText? musicLabel;
    private string effectLabelText = string.Empty;
    private string masterLabelText = string.Empty;
    private string musicLabelText = string.Empty;
    private bool remapped;

    [BackgroundDependencyLoader(true)]
    private void load(VolumeOverlay? overlay, AudioManager audio, KaraokeRulesetConfigManager config)
    {
        if (overlay == null)
            return;

        volumeOverlay = overlay;
        audioManager = audio;
        vocalConfig.BindTo(config.GetBindable<float>(KaraokeRulesetSetting.GuideVoiceVolume));
        microphoneConfig.BindTo(config.GetBindable<float>(KaraokeRulesetSetting.MicrophoneMonitorVolume));
        backgroundMusicConfig.BindTo(config.GetBindable<double>(KaraokeRulesetSetting.BackgroundMusicVolume));
        bindAdapter(vocalVolume, vocalConfig);
        bindAdapter(microphoneVolume, microphoneConfig);

        // The global overlay is already loaded by the time a ruleset player is
        // created, so mutate it on its update thread.
        Schedule(remap);
    }

    private void remap()
    {
        if (volumeOverlay == null || audioManager == null)
            return;

        VolumeMeter[] meters = volumeOverlay.ChildrenOfType<VolumeMeter>().ToArray();
        masterMeter = meters.OfType<MasterVolumeMeter>().SingleOrDefault();
        VolumeMeter[] smallerMeters = meters.Where(meter => meter is not MasterVolumeMeter).Take(2).ToArray();
        if (masterMeter == null || smallerMeters.Length != 2)
            return;

        effectMeter = smallerMeters[0];
        musicMeter = smallerMeters[1];

        effectMeter.Bindable.UnbindFrom(audioManager.VolumeSample);
        masterMeter.Bindable.UnbindFrom(audioManager.Volume);
        musicMeter.Bindable.UnbindFrom(audioManager.VolumeTrack);

        effectMeter.Bindable.BindTo(microphoneVolume);
        masterMeter.Bindable.BindTo(backgroundMusicConfig);
        musicMeter.Bindable.BindTo(vocalVolume);

        effectLabel = findLabel(effectMeter);
        masterLabel = findLabel(masterMeter);
        musicLabel = findLabel(musicMeter);
        effectLabelText = effectLabel?.Text.ToString() ?? string.Empty;
        masterLabelText = masterLabel?.Text.ToString() ?? string.Empty;
        musicLabelText = musicLabel?.Text.ToString() ?? string.Empty;

        if (effectLabel != null) effectLabel.Text = "MY VOICE";
        if (masterLabel != null) masterLabel.Text = "BGM";
        if (musicLabel != null) musicLabel.Text = "ORIGINAL VOCALS";
        remapped = true;
    }

    private static OsuSpriteText? findLabel(VolumeMeter meter)
        => meter.ChildrenOfType<OsuSpriteText>().LastOrDefault();

    private static void bindAdapter(BindableDouble adapter, BindableFloat config)
    {
        config.BindValueChanged(value => adapter.Value = value.NewValue, true);
        adapter.BindValueChanged(value => config.Value = (float)value.NewValue);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (remapped && audioManager != null)
        {
            effectMeter?.Bindable.UnbindFrom(microphoneVolume);
            masterMeter?.Bindable.UnbindFrom(backgroundMusicConfig);
            musicMeter?.Bindable.UnbindFrom(vocalVolume);

            effectMeter?.Bindable.BindTo(audioManager.VolumeSample);
            masterMeter?.Bindable.BindTo(audioManager.Volume);
            musicMeter?.Bindable.BindTo(audioManager.VolumeTrack);

            if (effectLabel != null) effectLabel.Text = effectLabelText;
            if (masterLabel != null) masterLabel.Text = masterLabelText;
            if (musicLabel != null) musicLabel.Text = musicLabelText;
        }

        vocalVolume.UnbindAll();
        microphoneVolume.UnbindAll();
        vocalConfig.UnbindAll();
        microphoneConfig.UnbindAll();
        backgroundMusicConfig.UnbindAll();
        base.Dispose(isDisposing);
    }
}
