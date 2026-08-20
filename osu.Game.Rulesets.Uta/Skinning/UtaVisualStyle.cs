// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Rulesets.Uta.UI.HUD;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.Skinning;

public sealed record UtaVisualStyle(
    UtaPitchStyle Pitch,
    UtaLyricsStyle Lyrics,
    UtaFeedbackStyle Feedback,
    UtaMotionStyle Motion,
    UtaSkinAssets Assets,
    UtaHudDensity Density)
{
    public static UtaVisualStyle Prism(UtaHudDensity density = UtaHudDensity.Standard, bool reducedMotion = false)
        => UtaSkinStyleResolver.CreatePrism(density, reducedMotion);
}

public sealed record UtaPitchStyle(
    Color4 Panel,
    Color4 GridMajor,
    Color4 GridMinor,
    Color4 Axis,
    Color4 Target,
    Color4 TargetGolden,
    Color4 TargetFreestyle,
    Color4 TargetRap,
    Color4 TargetSpoken,
    Color4 Reference,
    Color4 LiveNeutral,
    Color4 LiveAccurate,
    Color4 LiveNear,
    Color4 LiveOff,
    Color4 Playhead,
    float GridMajorWeight,
    float GridMinorWeight,
    float ReferenceCurveWeight,
    float LiveCurveWeight,
    float TrailWeight,
    float TrailGlow,
    float TargetNoteHeight,
    float TargetNoteBorder,
    float TargetNoteCornerRadius,
    float Opacity);

public sealed record UtaLyricsStyle(
    Color4 Panel,
    Color4 Current,
    Color4 Sung,
    Color4 Estimated,
    Color4 Reading,
    Color4 Upcoming,
    Color4 Countdown,
    Color4 Outline,
    float CurrentSize,
    float ReadingSize,
    float UpcomingSize,
    float PanelOpacity,
    float ProgressThickness);

public sealed record UtaFeedbackStyle(Color4 Perfect, Color4 Great, Color4 Good, Color4 Bad, Color4 Miss);

public sealed record UtaMotionStyle(
    float AnimationIntensity,
    double NotePulseMilliseconds,
    double LyricsTokenPulseMilliseconds,
    double PanelTransitionMilliseconds,
    int MaxSingingParticles,
    int MaxScoringParticles,
    bool ReducedMotion);

public sealed record UtaSkinAssets(
    Texture? PitchPanel,
    Texture? TargetNormal,
    Texture? TargetGolden,
    Texture? TargetFreestyle,
    Texture? TargetRap,
    Texture? TargetSpoken,
    Texture? TargetGoldenFreestyle,
    Texture? TargetGoldenRap,
    Texture? TargetGoldenSpoken,
    Texture? Playhead,
    Texture? GridMajor,
    Texture? GridMinor,
    Texture? CurveReference,
    Texture? CurveLive,
    Texture? CurveTrail,
    Texture? LyricsPanel,
    Texture? LyricsUnderline,
    Texture? LyricsReadingMarker,
    Texture? LyricsProgress,
    Texture? LyricsUpcomingMarker,
    Texture? FeedbackPerfect,
    Texture? FeedbackGreat,
    Texture? FeedbackGood,
    Texture? FeedbackBad,
    Texture? FeedbackMiss,
    Texture? FaultHigh,
    Texture? FaultLow,
    Texture? FaultUnstable,
    Texture? FaultCoverage,
    Texture? FaultInaccurate,
    Texture? ParticleSing,
    Texture? ParticleScore,
    Texture? HudPanel,
    Texture? HudAccent)
{
    public Texture? FeedbackFor(UtaNoteGrade grade) => grade switch
    {
        UtaNoteGrade.Perfect => FeedbackPerfect,
        UtaNoteGrade.Great => FeedbackGreat,
        UtaNoteGrade.Good => FeedbackGood,
        UtaNoteGrade.Bad => FeedbackBad,
        _ => FeedbackMiss,
    };

    public Texture? FaultFor(UtaPitchFault faults)
    {
        if (faults.HasFlag(UtaPitchFault.High)) return FaultHigh;
        if (faults.HasFlag(UtaPitchFault.Low)) return FaultLow;
        if (faults.HasFlag(UtaPitchFault.Unstable)) return FaultUnstable;
        if (faults.HasFlag(UtaPitchFault.LowCoverage)) return FaultCoverage;
        if (faults.HasFlag(UtaPitchFault.Inaccurate)) return FaultInaccurate;
        return null;
    }

    public Texture? TargetFor(string noteKind)
    {
        string normalised = noteKind.Replace('-', '_').ToLowerInvariant();
        if (normalised.Contains("golden_freestyle", StringComparison.Ordinal))
            return TargetGoldenFreestyle ?? TargetGolden ?? TargetFreestyle ?? TargetNormal;
        if (normalised.Contains("golden_rap", StringComparison.Ordinal))
            return TargetGoldenRap ?? TargetGolden ?? TargetRap ?? TargetNormal;
        if (normalised.Contains("golden_spoken", StringComparison.Ordinal))
            return TargetGoldenSpoken ?? TargetGolden ?? TargetSpoken ?? TargetNormal;
        if (normalised.Contains("golden", StringComparison.Ordinal))
            return TargetGolden ?? TargetNormal;
        if (normalised.Contains("freestyle", StringComparison.Ordinal))
            return TargetFreestyle ?? TargetNormal;
        if (normalised.Contains("rap", StringComparison.Ordinal))
            return TargetRap ?? TargetNormal;
        if (normalised.Contains("spoken", StringComparison.Ordinal))
            return TargetSpoken ?? TargetNormal;
        return TargetNormal;
    }
}

internal sealed class UtaVisualStyleProvider
{
    public UtaVisualStyle Style { get; private set; } = UtaVisualStyle.Prism();

    public event Action<UtaVisualStyle>? StyleChanged;

    public void Set(UtaVisualStyle style)
    {
        if (Style == style)
            return;

        Style = style;
        StyleChanged?.Invoke(style);
    }
}
