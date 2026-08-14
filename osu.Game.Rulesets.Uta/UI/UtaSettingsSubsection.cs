// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;

namespace osu.Game.Rulesets.Uta;

public sealed partial class UtaSettingsSubsection : RulesetSettingsSubsection
{
    protected override LocalisableString Header => "uta!";

    public UtaSettingsSubsection(UtaRuleset ruleset)
        : base(ruleset)
    {
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var config = (UtaRulesetConfigManager)Config;

        Children = new Drawable[]
        {
            new SettingsItemV2(new FormEnumDropdown<UtaLyricsPosition>
            {
                Caption = "Lyrics position",
                HintText = "Place lyrics at the top, centre or bottom of the playfield.",
                Current = config.GetBindable<UtaLyricsPosition>(UtaRulesetSetting.LyricsPosition),
            }),
            new SettingsItemV2(new FormEnumDropdown<UtaLyricsSize>
            {
                Caption = "Lyrics size",
                HintText = "Scale the current line, reading text and upcoming line together.",
                Current = config.GetBindable<UtaLyricsSize>(UtaRulesetSetting.LyricsSize),
            }),
            new SettingsItemV2(new LyricsTypefaceDropdown
            {
                Caption = "Lyrics font",
                HintText = "Use one of lazer's bundled typefaces; missing glyphs still use its fallback fonts.",
                Current = config.GetBindable<UtaLyricsTypeface>(UtaRulesetSetting.LyricsTypeface),
            }),
            new SettingsItemV2(new PitchCurveDisplayDropdown
            {
                Caption = "Pitch curves",
                HintText = "Show the song analysis, your detected pitch, both curves, or neither.",
                Current = config.GetBindable<UtaPitchCurveDisplay>(UtaRulesetSetting.PitchCurveDisplay),
            }),
            new SettingsItemV2(new FormCheckBox
            {
                Caption = "Singing guide trail",
                HintText = "Show the thicker glowing trail used by the earlier pitch guide.",
                Current = config.GetBindable<bool>(UtaRulesetSetting.ShowPitchGuideTrail),
            }),
            output("BGM output", "Hardware output used by the instrumental track.", config.GetBindable<string>(UtaRulesetSetting.BackgroundMusicOutputDevice)),
            output("Vocals output", "Hardware output used by the original or guide-vocal track.", config.GetBindable<string>(UtaRulesetSetting.OriginalVocalsOutputDevice)),
            output("Microphone monitor output", "Hardware output used for live microphone monitoring.", config.GetBindable<string>(UtaRulesetSetting.MicrophoneOutputDevice)),
            new SettingsItemV2(new MicrophoneDropdown
            {
                Caption = "Microphone",
                HintText = "The input device used for live pitch detection.",
                Current = config.GetBindable<string>(UtaRulesetSetting.MicrophoneDevice),
                Items = new[] { string.Empty }.Concat(UtaMicrophoneDevices.Enumerate().Select(device => device.Name)),
            }),
            slider("Microphone input gain", "Software gain applied before pitch detection and monitoring.",
                config.GetBindable<float>(UtaRulesetSetting.MicrophoneInputGain), value => $"{value:0.00}×", 0.05f),
            slider("Microphone monitor", "Hear your microphone through the active output. Headphones are recommended.",
                config.GetBindable<float>(UtaRulesetSetting.MicrophoneMonitorVolume), value => $"{value:P0}", 0.05f),
            slider("Background music", "Volume of the instrumental track during Uta gameplay.",
                config.GetBindable<double>(UtaRulesetSetting.BackgroundMusicVolume), value => $"{value:P0}", 0.05f),
            slider("Original vocals", "Volume of the independently routed guide-vocal or original track.",
                config.GetBindable<float>(UtaRulesetSetting.OriginalVocalsVolume), value => $"{value:P0}", 0.05f),
        };
    }

    private static SettingsItemV2 output(LocalisableString caption, LocalisableString hint, osu.Framework.Bindables.Bindable<string> current)
        => new(new AudioOutputDropdown
        {
            Caption = caption,
            HintText = hint,
            Current = current,
            Items = new[] { string.Empty }.Concat(UtaAudioDevices.Enumerate().Select(device => device.Name)).Distinct(),
        });

    private static SettingsItemV2 slider<T>(LocalisableString caption, LocalisableString hint, osu.Framework.Bindables.Bindable<T> current,
                                             System.Func<T, LocalisableString> format, float keyboardStep)
        where T : struct, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
        => new(new FormSliderBar<T>
        {
            Caption = caption,
            HintText = hint,
            Current = current,
            LabelFormat = format,
            KeyboardStep = keyboardStep,
        });

    private sealed partial class MicrophoneDropdown : FormDropdown<string>
    {
        protected override LocalisableString GenerateItemText(string item)
            => string.IsNullOrEmpty(item) ? "System default" : item;
    }

    private sealed partial class LyricsTypefaceDropdown : FormEnumDropdown<UtaLyricsTypeface>
    {
        protected override LocalisableString GenerateItemText(UtaLyricsTypeface item)
            => item == UtaLyricsTypeface.TorusAlternate ? "Torus Alternate" : item.ToString();
    }

    private sealed partial class PitchCurveDisplayDropdown : FormEnumDropdown<UtaPitchCurveDisplay>
    {
        protected override LocalisableString GenerateItemText(UtaPitchCurveDisplay item)
            => item switch
            {
                UtaPitchCurveDisplay.MyVoice => "My voice",
                UtaPitchCurveDisplay.Both => "Song + my voice",
                _ => item.ToString(),
            };
    }

    private sealed partial class AudioOutputDropdown : FormDropdown<string>
    {
        protected override LocalisableString GenerateItemText(string item)
            => string.IsNullOrEmpty(item) ? "Lazer default" : item;
    }
}
