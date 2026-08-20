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

        SetDefault(UtaRulesetSetting.RemoteControlPort, 27835, 1024, 65535);
        SetDefault(UtaRulesetSetting.ReducedMotion, false);
        SetDefault(UtaRulesetSetting.VideoVisible, true);
        SetDefault(UtaRulesetSetting.VideoDim, 0.35f, 0f, 1f, 0.01f);
        SetDefault(UtaRulesetSetting.VideoBlur, 0f, 0f, 1f, 0.01f);
        SetDefault(UtaRulesetSetting.VideoOffset, 0f, -5000f, 5000f, 1f);
        SetDefault(UtaRulesetSetting.ParticleIntensity, 0.65f, 0f, 1f, 0.05f);
        SetDefault(UtaRulesetSetting.PitchHudSize, UtaPitchHudSize.Normal);
        SetDefault(UtaRulesetSetting.PitchHudOpacity, 1f, 0.5f, 1f, 0.05f);
        SetDefault(UtaRulesetSetting.PitchHudLayout, UtaPitchHudLayout.Auto);
        SetDefault(UtaRulesetSetting.LyricsShowUpcoming, true);
        SetDefault(UtaRulesetSetting.LyricsShowReading, true);
        SetDefault(UtaRulesetSetting.LyricsPanelOpacity, 0.72f, 0f, 0.95f, 0.05f);
        SetDefault(UtaRulesetSetting.LyricsProgressStyle, UtaLyricsProgressStyle.Underline);
        SetDefault(UtaRulesetSetting.HudSafeAreaPadding, 0f, 0f, 64f, 1f);
        SetDefault(UtaRulesetSetting.OriginalVocalsEnabled, false);
        SetDefault(UtaRulesetSetting.StageEffectStyle, UtaStageEffectStyle.Fireflies);
    }

}

/// <summary>
/// Values 0-22 are the historical 0.7.2 wire keys and must never be renumbered.
/// New settings are appended explicitly so persisted configuration remains readable.
/// </summary>
public enum UtaRulesetSetting
{
    BackgroundMusicVolume = 0,
    OriginalVocalsVolume = 1,
    BackgroundMusicOutputDevice = 2,
    OriginalVocalsOutputDevice = 3,
    MicrophoneDevice = 4,
    MicrophoneOutputDevice = 5,
    MicrophoneInputGain = 6,
    MicrophoneMonitorVolume = 7,
    LyricsPosition = 8,
    LyricsSize = 9,
    LyricsTypeface = 10,
    PitchCurveDisplay = 11,
    ShowPitchGuideTrail = 12,
    MicrophoneLatency = 13,
    KeyShiftSemitones = 14,
    AccompanimentLatency = 15,
    LyricsLatency = 16,
    DebugDiagnostics = 17,
    PitchSamplingInterval = 18,
    PhraseLoopLeadIn = 19,
    // Retained as a legacy key so existing numeric ruleset settings keep their
    // stable values. Recording is controlled exclusively by Recording Mod.
    RecordMicrophone = 20,
    PerformanceRootDirectory = 21,
    ScoreHudPosition = 22,

    // Reserved values from an abandoned development-only migration. Runtime code uses key 5.
    Reserved23 = 23,
    Reserved24 = 24,
    RemoteControlPort = 25,
    ReducedMotion = 26,
    VideoVisible = 27,
    VideoDim = 28,
    VideoBlur = 29,
    VideoOffset = 30,
    ParticleIntensity = 31,
    PitchHudSize = 32,
    PitchHudOpacity = 33,
    PitchHudLayout = 34,
    LyricsShowUpcoming = 35,
    LyricsShowReading = 36,
    LyricsPanelOpacity = 37,
    LyricsProgressStyle = 38,
    HudSafeAreaPadding = 39,
    OriginalVocalsEnabled = 40,
    StageEffectStyle = 41,
}

public enum UtaStageEffectStyle
{
    Fireflies,
    Starlight,
    Confetti,
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

public enum UtaPitchHudSize
{
    Compact,
    Normal,
    Large,
}

public enum UtaPitchHudLayout
{
    Auto,
    FullWidth,
    Inset,
}

public enum UtaLyricsProgressStyle
{
    Underline,
    Fill,
    Marker,
}
