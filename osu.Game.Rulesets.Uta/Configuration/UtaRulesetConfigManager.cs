// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.Uta.Configuration;

public sealed class UtaRulesetConfigManager : RulesetConfigManager<UtaRulesetSetting>
{
    public UtaRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset)
        : base(settings, ruleset)
    {
    }

    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();

        SetDefault(UtaRulesetSetting.BackgroundMusicVolume, 1.0, 0.0, 1.0, 0.01);
        SetDefault(UtaRulesetSetting.OriginalVocalsVolume, 0.55f, 0f, 1f, 0.01f);
        SetDefault(UtaRulesetSetting.BackgroundMusicOutputDevice, string.Empty);
        SetDefault(UtaRulesetSetting.OriginalVocalsOutputDevice, string.Empty);
        SetDefault(UtaRulesetSetting.MicrophoneDevice, string.Empty);
        SetDefault(UtaRulesetSetting.MicrophoneOutputDevice, string.Empty);
        SetDefault(UtaRulesetSetting.MicrophoneInputGain, 1.5f, 0.5f, 3f, 0.05f);
        SetDefault(UtaRulesetSetting.MicrophoneMonitorVolume, 0.35f, 0f, 1f, 0.01f);
        SetDefault(UtaRulesetSetting.MicrophoneLatency, 0f, -500f, 1000f, 1f);
        SetDefault(UtaRulesetSetting.KeyShiftSemitones, 0f, -6f, 6f, 1f);
        SetDefault(UtaRulesetSetting.AccompanimentLatency, 0f, -500f, 1000f, 1f);
        SetDefault(UtaRulesetSetting.LyricsLatency, 0f, -500f, 1000f, 1f);
        SetDefault(UtaRulesetSetting.DebugDiagnostics, false);
        SetDefault(UtaRulesetSetting.PitchSamplingInterval, 10f, 10f, 40f, 1f);
        SetDefault(UtaRulesetSetting.PhraseLoopLeadIn, 750f, 500f, 1000f, 50f);
        SetDefault(UtaRulesetSetting.PerformanceRootDirectory, string.Empty);
        SetDefault(UtaRulesetSetting.LyricsPosition, UtaLyricsPosition.Bottom);
        SetDefault(UtaRulesetSetting.LyricsSize, UtaLyricsSize.Normal);
        SetDefault(UtaRulesetSetting.LyricsTypeface, UtaLyricsTypeface.Torus);
        SetDefault(UtaRulesetSetting.PitchCurveDisplay, UtaPitchCurveDisplay.Both);
        SetDefault(UtaRulesetSetting.ShowPitchGuideTrail, false);
        SetDefault(UtaRulesetSetting.ScoreHudPosition, UtaScoreHudPosition.TopRight);
    }
}

public enum UtaRulesetSetting
{
    BackgroundMusicVolume,
    OriginalVocalsVolume,
    BackgroundMusicOutputDevice,
    OriginalVocalsOutputDevice,
    MicrophoneDevice,
    MicrophoneOutputDevice,
    MicrophoneInputGain,
    MicrophoneMonitorVolume,
    LyricsPosition,
    LyricsSize,
    LyricsTypeface,
    PitchCurveDisplay,
    ShowPitchGuideTrail,
    MicrophoneLatency,
    KeyShiftSemitones,
    AccompanimentLatency,
    LyricsLatency,
    DebugDiagnostics,
    PitchSamplingInterval,
    PhraseLoopLeadIn,
    // Retained as a legacy key so existing numeric ruleset settings keep their
    // stable values. Recording is now controlled exclusively by Recording Mod.
    RecordMicrophone,
    PerformanceRootDirectory,
    ScoreHudPosition,
}

public enum UtaScoreHudPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public enum UtaLyricsPosition
{
    Bottom,
    Centre,
    Top,
}

public enum UtaLyricsSize
{
    Compact,
    Normal,
    Large,
}

public enum UtaLyricsTypeface
{
    Torus,
    TorusAlternate,
    Inter,
}

public enum UtaPitchCurveDisplay
{
    Off,
    Song,
    MyVoice,
    Both,
}
