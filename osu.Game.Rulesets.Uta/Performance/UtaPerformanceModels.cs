// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Performance;

public sealed class UtaPerformanceManifest
{
    public const int LATEST_SCHEMA_VERSION = 1;

    public int SchemaVersion { get; set; } = LATEST_SCHEMA_VERSION;
    public Guid PerformanceId { get; set; }
    public Guid? LazerScoreId { get; set; }
    public string? LazerScoreHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public UtaPerformanceSongInfo Song { get; set; } = new();
    public UtaPerformanceScoringSummary Scoring { get; set; } = new();
    public UtaPerformanceJudgementSummary Judgements { get; set; } = new();
    public UtaPerformanceSettingsSnapshot Settings { get; set; } = new();
    public UtaPerformanceEligibility Eligibility { get; set; } = new();
    public IReadOnlyList<UtaPerformanceNoteSummary> Notes { get; set; } = Array.Empty<UtaPerformanceNoteSummary>();
    public IReadOnlyList<UtaPerformancePhraseSummary> Phrases { get; set; } = Array.Empty<UtaPerformancePhraseSummary>();
    public UtaPerformanceFileSet Files { get; set; } = new();
    public UtaPerformanceRecordingInfo? Recording { get; set; }
    public Dictionary<string, string> Checksums { get; set; } = new(StringComparer.Ordinal);

    public static UtaPerformanceManifest FromScore(
        UtaPerformanceSongInfo song,
        UtaPerformanceSettingsSnapshot settings,
        UtaPerformanceScore score,
        Guid? lazerScoreId = null)
        => new()
        {
            PerformanceId = Guid.NewGuid(),
            LazerScoreId = lazerScoreId,
            Song = song,
            Settings = settings,
            Scoring = UtaPerformanceScoringSummary.FromScore(score),
            Judgements = UtaPerformanceJudgementSummary.FromScore(score),
            Notes = score.Notes.Select(UtaPerformanceNoteSummary.FromScore).ToArray(),
        };
}

public sealed class UtaPerformanceSongInfo
{
    public string PackageId { get; set; } = string.Empty;
    public int PackageRevision { get; set; }
    public string BeatmapHash { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
}

public sealed class UtaPerformanceScoringSummary
{
    public string Engine { get; set; } = "uta.pitch";
    public int EngineVersion { get; set; } = UtaScoringOptions.ENGINE_VERSION;
    public long TotalScore { get; set; }
    public ushort CompositeRatingPermille { get; set; }
    public ushort PitchAccuracyPermille { get; set; }
    public ushort CoveragePermille { get; set; }
    public ushort StabilityPermille { get; set; }
    public ushort LongToneQualityPermille { get; set; }
    public ushort VibratoQualityPermille { get; set; }
    public ushort? ExpressionQualityPermille { get; set; }
    public UtaScoringProfile Profile { get; set; }
    public UtaAnalysisMessage PositiveMessage { get; set; }
    public UtaAnalysisMessage AdviceMessage { get; set; }
    public int HighestCombo { get; set; }
    public int HighestAccurateStreak { get; set; }

    public static UtaPerformanceScoringSummary FromScore(UtaPerformanceScore score)
        => new()
        {
            TotalScore = score.TotalScore,
            CompositeRatingPermille = score.Profiles.FinalPermille,
            PitchAccuracyPermille = score.PitchAccuracyPermille,
            CoveragePermille = score.CoveragePermille,
            StabilityPermille = score.StabilityPermille,
            LongToneQualityPermille = score.LongToneQualityPermille,
            VibratoQualityPermille = score.VibratoQualityPermille,
            Profile = score.FinalProfile,
            PositiveMessage = score.Analysis.Positive,
            AdviceMessage = score.Analysis.Advice,
            HighestCombo = score.HighestCombo,
            HighestAccurateStreak = score.HighestAccurateStreak,
        };
}

public sealed class UtaPerformanceJudgementSummary
{
    public int Perfect { get; set; }
    public int Great { get; set; }
    public int Good { get; set; }
    public int Bad { get; set; }
    public int Miss { get; set; }
    public int Ignored { get; set; }
    public int High { get; set; }
    public int Low { get; set; }
    public int Unstable { get; set; }
    public int Inaccurate { get; set; }
    public int LowCoverage { get; set; }

    public static UtaPerformanceJudgementSummary FromScore(UtaPerformanceScore score)
        => new()
        {
            Perfect = countGrade(score, UtaNoteGrade.Perfect),
            Great = countGrade(score, UtaNoteGrade.Great),
            Good = countGrade(score, UtaNoteGrade.Good),
            Bad = countGrade(score, UtaNoteGrade.Bad),
            Miss = countGrade(score, UtaNoteGrade.Miss),
            Ignored = countGrade(score, UtaNoteGrade.Ignored),
            High = countFault(score, UtaPitchFault.High),
            Low = countFault(score, UtaPitchFault.Low),
            Unstable = countFault(score, UtaPitchFault.Unstable),
            Inaccurate = countFault(score, UtaPitchFault.Inaccurate),
            LowCoverage = countFault(score, UtaPitchFault.LowCoverage),
        };

    private static int countGrade(UtaPerformanceScore score, UtaNoteGrade grade)
        => score.GradeCounts.TryGetValue(grade, out int value) ? value : 0;

    private static int countFault(UtaPerformanceScore score, UtaPitchFault fault)
        => score.FaultCounts.TryGetValue(fault, out int value) ? value : 0;
}

public sealed class UtaPerformanceSettingsSnapshot
{
    public int TransposeSemitones { get; set; }
    public bool OctaveFold { get; set; }
    public double PlaybackRate { get; set; } = 1;
    public double MicrophoneLatencyMilliseconds { get; set; }
    public double PitchSamplingIntervalMilliseconds { get; set; }
    public double InputGain { get; set; } = 1;
    public bool PracticeSession { get; set; }
    public int TimelineEpoch { get; set; }
    public long ScoringBinMicroseconds { get; set; } = UtaScoringOptions.DEFAULT_BIN_MICROSECONDS;
    public long CommitDelayMicroseconds { get; set; } = UtaScoringOptions.DEFAULT_COMMIT_DELAY_MICROSECONDS;
    public ushort MinimumClarityPermille { get; set; } = 550;
}

public enum UtaPerformanceInvalidationReason
{
    PracticeSession,
    TimelineDiscontinuity,
    ScoringQueueOverflow,
    SettingsChangedDuringPlay,
    CaptureUnavailable,
    IncompletePerformance,
}

public sealed class UtaPerformanceEligibility
{
    public bool Comparable { get; set; } = true;
    public IReadOnlyList<UtaPerformanceInvalidationReason> InvalidationReasons { get; set; } = Array.Empty<UtaPerformanceInvalidationReason>();

    public static UtaPerformanceEligibility Ineligible(params UtaPerformanceInvalidationReason[] reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        if (reasons.Length == 0)
            throw new ArgumentException("At least one invalidation reason is required.", nameof(reasons));

        return new UtaPerformanceEligibility
        {
            Comparable = false,
            InvalidationReasons = reasons.Distinct().OrderBy(reason => reason).ToArray(),
        };
    }
}

public sealed class UtaPerformanceNoteSummary
{
    public int ScoringIndex { get; set; }
    public long StartTimeMicroseconds { get; set; }
    public long EndTimeMicroseconds { get; set; }
    public UtaNoteGrade Grade { get; set; }
    public UtaPitchFault Faults { get; set; }
    public ushort PitchAccuracyPermille { get; set; }
    public ushort CoveragePermille { get; set; }
    public ushort StabilityPermille { get; set; }
    public ushort TechniqueQualityPermille { get; set; }
    public int BiasCents { get; set; }
    public UtaVibratoResult Vibrato { get; set; }

    public static UtaPerformanceNoteSummary FromScore(UtaNoteScore score)
        => new()
        {
            ScoringIndex = score.Target.Index,
            StartTimeMicroseconds = score.Target.StartTimeMicroseconds,
            EndTimeMicroseconds = score.Target.EndTimeMicroseconds,
            Grade = score.Grade,
            Faults = score.Faults,
            PitchAccuracyPermille = score.PitchAccuracyPermille,
            CoveragePermille = score.CoveragePermille,
            StabilityPermille = score.StabilityPermille,
            TechniqueQualityPermille = score.TechniqueQualityPermille,
            BiasCents = score.BiasCents,
            Vibrato = score.Vibrato,
        };
}

public sealed class UtaPerformancePhraseSummary
{
    public int PhraseIndex { get; set; }
    public long StartTimeMicroseconds { get; set; }
    public long EndTimeMicroseconds { get; set; }
    public string Text { get; set; } = string.Empty;
    public ushort PitchAccuracyPermille { get; set; }
    public ushort CoveragePermille { get; set; }
    public ushort StabilityPermille { get; set; }
    public int BiasCents { get; set; }
    public IReadOnlyList<UtaMissedInterval> MissedIntervals { get; set; } = Array.Empty<UtaMissedInterval>();
}

public readonly record struct UtaMissedInterval(long StartTimeMicroseconds, long EndTimeMicroseconds);


public sealed class UtaPerformanceRecordingInfo
{
    public string Container { get; set; } = "wav";
    public string SampleFormat { get; set; } = "pcm_s16le";
    public int SampleRate { get; set; } = 48_000;
    public int Channels { get; set; } = 1;
    public long StartSongTimeMicroseconds { get; set; }
    public double CalibratedLatencyMilliseconds { get; set; }
    public double InputGain { get; set; } = 1;
    public string SignalStage { get; set; } = "post_input_gain_pre_monitor";
}

public sealed class UtaPerformanceFileSet
{
    public string? PitchReplay { get; set; }
    public string? Recording { get; set; }
    public string? Waveform { get; set; }
}

public readonly record struct UtaPerformancePitchFrame(
    long TimeMicroseconds,
    int PitchCents,
    ushort ClarityPermille,
    short? RmsDecibelsTenths,
    bool Voiced,
    int TimelineEpoch = 0)
{
    public static UtaPerformancePitchFrame FromMapped(UtaCapturedPitchFrame captured, UtaScoringFrame mapped)
        => new(
            mapped.TimeMicroseconds,
            mapped.PitchCents,
            mapped.ClarityPermille,
            captured.RmsDecibelsTenths,
            mapped.Voiced,
            mapped.TimelineEpoch);

    public UtaScoringFrame ToScoringFrame()
        => new(TimeMicroseconds, PitchCents, ClarityPermille, Voiced, TimelineEpoch);
}

public sealed record UtaPerformanceArchiveEntry(
    string DirectoryPath,
    UtaPerformanceManifest Manifest,
    bool IndexUpdated);

public sealed record UtaPerformanceIndexItem(
    Guid PerformanceId,
    Guid? LazerScoreId,
    DateTimeOffset CreatedAtUtc,
    string PackageId,
    string BeatmapHash,
    long TotalScore,
    string RelativeDirectory);

public sealed class UtaPerformanceIndex
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<UtaPerformanceIndexItem> Items { get; set; } = Array.Empty<UtaPerformanceIndexItem>();
}

public sealed record UtaPerformanceWriteRequest(
    UtaPerformanceManifest Manifest,
    IEnumerable<UtaPerformancePitchFrame> PitchFrames,
    System.IO.Stream? Recording = null,
    string RecordingFileName = "take.wav",
    System.IO.Stream? Waveform = null,
    string WaveformFileName = "waveform.bin");
