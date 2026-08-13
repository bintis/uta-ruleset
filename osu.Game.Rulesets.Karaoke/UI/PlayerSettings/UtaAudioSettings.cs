// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Karaoke.Configuration;
using osu.Game.Screens.Play.PlayerSettings;

namespace osu.Game.Rulesets.Karaoke.UI.PlayerSettings;

public partial class UtaAudioSettings : PlayerSettingsGroup
{
    private readonly PlayerSliderBar<double> backgroundMusicVolume;
    private readonly PlayerSliderBar<float> guideVoiceVolume;
    private readonly PlayerSliderBar<float> microphoneInputGain;
    private readonly PlayerSliderBar<float> monitorVolume;

    public UtaAudioSettings()
        : base("Karaoke audio")
    {
        Children = new Drawable[]
        {
            backgroundMusicVolume = new PlayerSliderBar<double>
            {
                LabelText = "BGM",
                TooltipText = "Volume of this song's instrumental track.",
                KeyboardStep = 0.05f,
                DisplayAsPercentage = true,
            },
            guideVoiceVolume = new PlayerSliderBar<float>
            {
                LabelText = "Original vocals",
                TooltipText = "Volume of the original or guide-vocal track enabled by the VOX mod.",
                KeyboardStep = 0.05f,
                DisplayAsPercentage = true,
            },
            microphoneInputGain = new PlayerSliderBar<float>
            {
                LabelText = "Microphone input gain",
                TooltipText = "Software boost applied before pitch detection and microphone monitoring.",
                KeyboardStep = 0.05f,
                DisplayAsPercentage = true,
            },
            monitorVolume = new PlayerSliderBar<float>
            {
                LabelText = "My voice",
                TooltipText = "Hear your microphone through the active output. Headphones are recommended.",
                KeyboardStep = 0.05f,
                DisplayAsPercentage = true,
            },
        };
    }

    [BackgroundDependencyLoader]
    private void load(KaraokeRulesetConfigManager config)
    {
        backgroundMusicVolume.Current = config.GetBindable<double>(KaraokeRulesetSetting.BackgroundMusicVolume);
        guideVoiceVolume.Current = config.GetBindable<float>(KaraokeRulesetSetting.GuideVoiceVolume);
        microphoneInputGain.Current = config.GetBindable<float>(KaraokeRulesetSetting.MicrophoneInputGain);
        monitorVolume.Current = config.GetBindable<float>(KaraokeRulesetSetting.MicrophoneMonitorVolume);
    }
}
