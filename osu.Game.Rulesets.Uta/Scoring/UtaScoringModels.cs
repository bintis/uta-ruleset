// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Uta.Pitch;

namespace osu.Game.Rulesets.Uta.Scoring;

public enum UtaScoringNoteKind
{
    Normal,
    Golden,
    Freestyle,
    GoldenFreestyle,
    Rap,
    GoldenRap,
    Spoken,
    GoldenSpoken,
}

/// <summary>
/// User-facing quality band. Directional pitch faults are deliberately kept
/// separate so a Bad note can explain whether it was high, low or unstable.
/// </summary>
public enum UtaNoteGrade
{
    Ignored,
    Perfect,
    Great,
    Good,
    Bad,
    Miss,
}

[Flags]
public enum UtaPitchFault
{
    None = 0,
    High = 1 << 0,
    Low = 1 << 1,
    Unstable = 1 << 2,
    Inaccurate = 1 << 3,
    LowCoverage = 1 << 4,
}

public enum UtaScoringProfile
{
    Faithful,
    Stable,
    Technique,
}

public enum UtaAnalysisMessage
{
    None,
    AccurateAndStable,
    AccuratePitch,
    StrongLongTones,
    ControlledVibrato,
    ConsistentVoicing,
    ImproveCoverage,
    LowerPitch,
    RaisePitch,
    ImproveStability,
    ImprovePitchAccuracy,
    ReduceBadNotes,
}

public readonly record struct UtaScoringTarget(
    int Index,
    long StartTimeMicroseconds,
    long EndTimeMicroseconds,
    int? Midi,
    ushort ConfidencePermille,
    UtaScoringNoteKind Kind)
{
    public long DurationMicroseconds => EndTimeMicroseconds - StartTimeMicroseconds;

    public static UtaScoringTarget FromConfidence(
        int index,
        long startTimeMicroseconds,
        long endTimeMicroseconds,
        int? midi,
        double confidence,
        UtaScoringNoteKind kind)
    {
        if (!double.IsFinite(confidence))
            throw new ArgumentOutOfRangeException(nameof(confidence));

        return new UtaScoringTarget(
            index,
            startTimeMicroseconds,
            endTimeMicroseconds,
            midi,
            checked((ushort)Math.Round(Math.Clamp(confidence, 0, 1) * UtaScoringOptions.QUALITY_SCALE, MidpointRounding.AwayFromZero)),
            kind);
    }
}

/// <summary>
/// Deterministic input contract for scoring. Time, pitch and confidence are
/// quantised before entering the scoring kernel.
/// </summary>
public readonly record struct UtaScoringFrame(
    long TimeMicroseconds,
    int PitchCents,
    ushort ClarityPermille,
    bool Voiced,
    int TimelineEpoch = 0)
{
    public static UtaScoringFrame FromHertz(double songTimeMilliseconds, double? hertz, double clarity, int timelineEpoch = 0)
    {
        if (!double.IsFinite(songTimeMilliseconds))
            throw new ArgumentOutOfRangeException(nameof(songTimeMilliseconds));
        if (!double.IsFinite(clarity))
            throw new ArgumentOutOfRangeException(nameof(clarity));
        if (timelineEpoch < 0)
            throw new ArgumentOutOfRangeException(nameof(timelineEpoch));

        long time = checked((long)Math.Round(songTimeMilliseconds * 1000, MidpointRounding.AwayFromZero));
        bool voiced = hertz is { } value && UtaPitchMath.IsFinitePitch(value);
        int pitchCents = voiced
            ? checked((int)Math.Round(UtaPitchMath.FrequencyToMidi(hertz!.Value) * 100, MidpointRounding.AwayFromZero))
            : 0;
        ushort clarityPermille = checked((ushort)Math.Round(Math.Clamp(clarity, 0, 1) * UtaScoringOptions.QUALITY_SCALE, MidpointRounding.AwayFromZero));
        return new UtaScoringFrame(time, pitchCents, clarityPermille, voiced, timelineEpoch);
    }
}

public sealed class UtaScoringOptions
{
    public const int ENGINE_VERSION = 2;
    public const int QUALITY_SCALE = 1000;
    public const long MAX_SCORE = 1_000_000;

    public const long DEFAULT_BIN_MICROSECONDS = 20_000;
    public const long DEFAULT_MAXIMUM_INTERPOLATION_GAP_MICROSECONDS = 80_000;
    public const long DEFAULT_MAXIMUM_NEAREST_FRAME_DISTANCE_MICROSECONDS = 40_000;
    public const long DEFAULT_NOTE_EDGE_MICROSECONDS = 60_000;
    public const long DEFAULT_MINIMUM_LONG_TONE_MICROSECONDS = 800_000;
    public const long DEFAULT_MINIMUM_VIBRATO_MICROSECONDS = 350_000;
    public const long DEFAULT_COMMIT_DELAY_MICROSECONDS = 60_000;

    public long BinDurationMicroseconds { get; init; } = DEFAULT_BIN_MICROSECONDS;

    public long MaximumInterpolationGapMicroseconds { get; init; } = DEFAULT_MAXIMUM_INTERPOLATION_GAP_MICROSECONDS;

    public long MaximumNearestFrameDistanceMicroseconds { get; init; } = DEFAULT_MAXIMUM_NEAREST_FRAME_DISTANCE_MICROSECONDS;

    public long NoteEdgeMicroseconds { get; init; } = DEFAULT_NOTE_EDGE_MICROSECONDS;

    public long MinimumLongToneMicroseconds { get; init; } = DEFAULT_MINIMUM_LONG_TONE_MICROSECONDS;

    public long MinimumVibratoMicroseconds { get; init; } = DEFAULT_MINIMUM_VIBRATO_MICROSECONDS;

    public long CommitDelayMicroseconds { get; init; } = DEFAULT_COMMIT_DELAY_MICROSECONDS;

    public ushort MinimumClarityPermille { get; init; } = 550;

    public ushort MinimumTargetConfidencePermille { get; init; } = 500;

    public ushort PerfectAccuracyPermille { get; init; } = 940;

    public ushort PerfectHitRatioPermille { get; init; } = 860;

    public ushort PerfectCoveragePermille { get; init; } = 850;

    public ushort GreatAccuracyPermille { get; init; } = 860;

    public ushort GreatCoveragePermille { get; init; } = 750;

    public ushort GoodAccuracyPermille { get; init; } = 700;

    public ushort GoodCoveragePermille { get; init; } = 600;

    public ushort MinimumVoicedPermille { get; init; } = 350;

    public ushort UnstableThresholdPermille { get; init; } = 550;

    public int DirectionalBiasCents { get; init; } = 35;

    public bool AllowOctaveTolerance { get; init; }

    public int TransposeSemitones { get; init; }

    public int TimelineEpoch { get; init; }

    internal void Validate()
    {
        if (BinDurationMicroseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(BinDurationMicroseconds));
        if (MaximumInterpolationGapMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumInterpolationGapMicroseconds));
        if (MaximumNearestFrameDistanceMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumNearestFrameDistanceMicroseconds));
        if (NoteEdgeMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(NoteEdgeMicroseconds));
        if (MinimumLongToneMicroseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumLongToneMicroseconds));
        if (MinimumVibratoMicroseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumVibratoMicroseconds));
        if (CommitDelayMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(CommitDelayMicroseconds));
        validatePermille(MinimumClarityPermille, nameof(MinimumClarityPermille));
        validatePermille(MinimumTargetConfidencePermille, nameof(MinimumTargetConfidencePermille));
        validatePermille(PerfectAccuracyPermille, nameof(PerfectAccuracyPermille));
        validatePermille(PerfectHitRatioPermille, nameof(PerfectHitRatioPermille));
        validatePermille(PerfectCoveragePermille, nameof(PerfectCoveragePermille));
        validatePermille(GreatAccuracyPermille, nameof(GreatAccuracyPermille));
        validatePermille(GreatCoveragePermille, nameof(GreatCoveragePermille));
        validatePermille(GoodAccuracyPermille, nameof(GoodAccuracyPermille));
        validatePermille(GoodCoveragePermille, nameof(GoodCoveragePermille));
        validatePermille(MinimumVoicedPermille, nameof(MinimumVoicedPermille));
        validatePermille(UnstableThresholdPermille, nameof(UnstableThresholdPermille));
        if (DirectionalBiasCents is < 0 or > 1200)
            throw new ArgumentOutOfRangeException(nameof(DirectionalBiasCents));
        if (TransposeSemitones is < -24 or > 24)
            throw new ArgumentOutOfRangeException(nameof(TransposeSemitones));
        if (TimelineEpoch < 0)
            throw new ArgumentOutOfRangeException(nameof(TimelineEpoch));
        if (GreatAccuracyPermille > PerfectAccuracyPermille || GoodAccuracyPermille > GreatAccuracyPermille)
            throw new ArgumentException("Grade accuracy thresholds must be monotonic.");
        if (GreatCoveragePermille > PerfectCoveragePermille || GoodCoveragePermille > GreatCoveragePermille || MinimumVoicedPermille > GoodCoveragePermille)
            throw new ArgumentException("Grade coverage thresholds must be monotonic.");
    }

    private static void validatePermille(ushort value, string name)
    {
        if (value > QUALITY_SCALE)
            throw new ArgumentOutOfRangeException(name);
    }
}

public readonly record struct UtaProfileRatings(
    ushort FaithfulPermille,
    ushort StablePermille,
    ushort TechniquePermille,
    ushort FinalPermille,
    UtaScoringProfile Profile);

public readonly record struct UtaVibratoResult(
    bool Detected,
    ushort QualityPermille,
    ushort CorrelationPermille,
    double RateHertz,
    int ExtentCents,
    int CentreDriftCentsPerSecond,
    long DurationMicroseconds);

public sealed record UtaNoteScore
{
    public required UtaScoringTarget Target { get; init; }
    public long MaximumUnits { get; init; }
    public long PitchEarnedUnits { get; init; }
    public long VoicedUnits { get; init; }
    public long HitUnits { get; init; }
    public long FaithfulEarnedUnits { get; init; }
    public long StableEarnedUnits { get; init; }
    public long TechniqueEarnedUnits { get; init; }
    public ushort PitchAccuracyPermille { get; init; }
    public ushort CoveragePermille { get; init; }
    public ushort HitRatioPermille { get; init; }
    public int BiasCents { get; init; }
    public ushort RawStabilityPermille { get; init; }
    public ushort StabilityPermille { get; init; }
    public ushort EffectiveStabilityPermille { get; init; }
    public ushort LongToneQualityPermille { get; init; }
    public ushort TechniqueQualityPermille { get; init; }
    public UtaVibratoResult Vibrato { get; init; }
    public UtaProfileRatings Profiles { get; init; }
    public UtaNoteGrade Grade { get; init; }
    public UtaPitchFault Faults { get; init; }

    public HitResult NativeResult => UtaNativeResultMapper.ToHitResult(Grade);
    public double PitchAccuracy => PitchAccuracyPermille / (double)UtaScoringOptions.QUALITY_SCALE;
    public double Coverage => CoveragePermille / (double)UtaScoringOptions.QUALITY_SCALE;
    public double HitRatio => HitRatioPermille / (double)UtaScoringOptions.QUALITY_SCALE;
    public double BiasSemitones => BiasCents / 100.0;
    public double Stability => StabilityPermille / (double)UtaScoringOptions.QUALITY_SCALE;
    public double TechniqueQuality => TechniqueQualityPermille / (double)UtaScoringOptions.QUALITY_SCALE;
}

public sealed record UtaAnalysisReport(UtaAnalysisMessage Positive, UtaAnalysisMessage Advice);

public sealed record UtaPerformanceScore
{
    public required IReadOnlyList<UtaNoteScore> Notes { get; init; }
    public long MaximumUnits { get; init; }
    public long PitchEarnedUnits { get; init; }
    public long VoicedUnits { get; init; }
    public long HitUnits { get; init; }
    public long FaithfulEarnedUnits { get; init; }
    public long StableEarnedUnits { get; init; }
    public long TechniqueEarnedUnits { get; init; }
    public ushort PitchAccuracyPermille { get; init; }
    public ushort CoveragePermille { get; init; }
    public ushort HitRatioPermille { get; init; }
    public int BiasCents { get; init; }
    public ushort RawStabilityPermille { get; init; }
    public ushort StabilityPermille { get; init; }
    public ushort LongToneQualityPermille { get; init; }
    public ushort VibratoQualityPermille { get; init; }
    public ushort TechniqueQualityPermille { get; init; }
    public UtaProfileRatings Profiles { get; init; }
    public long TotalScore { get; init; }
    public int HighestCombo { get; init; }
    public int HighestAccurateStreak { get; init; }
    public required IReadOnlyDictionary<UtaNoteGrade, int> GradeCounts { get; init; }
    public required IReadOnlyDictionary<UtaPitchFault, int> FaultCounts { get; init; }
    public required UtaAnalysisReport Analysis { get; init; }

    public double PitchAccuracy => PitchAccuracyPermille / (double)UtaScoringOptions.QUALITY_SCALE;
    public double Coverage => CoveragePermille / (double)UtaScoringOptions.QUALITY_SCALE;
    public double HitRatio => HitRatioPermille / (double)UtaScoringOptions.QUALITY_SCALE;
    public double BiasSemitones => BiasCents / 100.0;
    public double Stability => StabilityPermille / (double)UtaScoringOptions.QUALITY_SCALE;
    public double FinalRating => Profiles.FinalPermille / (double)UtaScoringOptions.QUALITY_SCALE;
    public UtaScoringProfile FinalProfile => Profiles.Profile;
}
