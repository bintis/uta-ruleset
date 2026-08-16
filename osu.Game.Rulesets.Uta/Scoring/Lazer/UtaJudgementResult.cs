// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.Uta.Scoring;

public sealed class UtaJudgementResult : JudgementResult
{
    public int ScoringIndex { get; set; } = -1;
    public int TimelineEpoch { get; set; }
    public UtaNoteGrade Grade { get; set; } = UtaNoteGrade.Ignored;
    public UtaPitchFault Faults { get; set; }
    public long MaximumUnits { get; set; }
    public long PitchEarnedUnits { get; set; }
    public long VoicedUnits { get; set; }
    public long HitUnits { get; set; }
    public long FaithfulEarnedUnits { get; set; }
    public long StableEarnedUnits { get; set; }
    public long TechniqueEarnedUnits { get; set; }
    public ushort PitchAccuracyPermille { get; set; }
    public ushort CoveragePermille { get; set; }
    public ushort HitRatioPermille { get; set; }
    public ushort RawStabilityPermille { get; set; }
    public ushort StabilityPermille { get; set; }
    public ushort LongToneQualityPermille { get; set; }
    public ushort TechniqueQualityPermille { get; set; }
    public UtaVibratoResult Vibrato { get; set; }
    public ushort FinalRatingPermille { get; set; }
    public UtaScoringProfile FinalProfile { get; set; }
    public int BiasCents { get; set; }
    public int AccurateStreakAtJudgement { get; internal set; }
    public int AccurateStreakAfterJudgement { get; internal set; }
    public int HighestAccurateStreakAtJudgement { get; internal set; }
    public int HighestAccurateStreakAfterJudgement { get; internal set; }

    public UtaJudgementResult(HitObject hitObject, Judgement judgement)
        : base(hitObject, judgement)
    {
    }

    public void Populate(UtaNoteScore score, int timelineEpoch)
    {
        ScoringIndex = score.Target.Index;
        TimelineEpoch = timelineEpoch;
        Grade = score.Grade;
        Faults = score.Faults;
        MaximumUnits = score.MaximumUnits;
        PitchEarnedUnits = score.PitchEarnedUnits;
        VoicedUnits = score.VoicedUnits;
        HitUnits = score.HitUnits;
        FaithfulEarnedUnits = score.FaithfulEarnedUnits;
        StableEarnedUnits = score.StableEarnedUnits;
        TechniqueEarnedUnits = score.TechniqueEarnedUnits;
        PitchAccuracyPermille = score.PitchAccuracyPermille;
        CoveragePermille = score.CoveragePermille;
        HitRatioPermille = score.HitRatioPermille;
        RawStabilityPermille = score.RawStabilityPermille;
        StabilityPermille = score.StabilityPermille;
        LongToneQualityPermille = score.LongToneQualityPermille;
        TechniqueQualityPermille = score.TechniqueQualityPermille;
        Vibrato = score.Vibrato;
        FinalRatingPermille = score.Profiles.FinalPermille;
        FinalProfile = score.Profiles.Profile;
        BiasCents = score.BiasCents;
        Type = score.NativeResult;
    }

    internal void PopulatePerfectSimulation(int scoringIndex, long maximumUnits)
    {
        ScoringIndex = scoringIndex;
        if (maximumUnits <= 0)
        {
            Grade = UtaNoteGrade.Ignored;
            Type = osu.Game.Rulesets.Scoring.HitResult.IgnoreHit;
            return;
        }

        Grade = UtaNoteGrade.Perfect;
        MaximumUnits = maximumUnits;
        PitchEarnedUnits = maximumUnits;
        VoicedUnits = maximumUnits;
        HitUnits = maximumUnits;
        FaithfulEarnedUnits = maximumUnits;
        StableEarnedUnits = maximumUnits;
        TechniqueEarnedUnits = maximumUnits;
        PitchAccuracyPermille = UtaScoringOptions.QUALITY_SCALE;
        CoveragePermille = UtaScoringOptions.QUALITY_SCALE;
        HitRatioPermille = UtaScoringOptions.QUALITY_SCALE;
        RawStabilityPermille = UtaScoringOptions.QUALITY_SCALE;
        StabilityPermille = UtaScoringOptions.QUALITY_SCALE;
        LongToneQualityPermille = UtaScoringOptions.QUALITY_SCALE;
        TechniqueQualityPermille = UtaScoringOptions.QUALITY_SCALE;
        FinalRatingPermille = UtaScoringOptions.QUALITY_SCALE;
        FinalProfile = UtaScoringProfile.Faithful;
        Type = Judgement.MaxResult;
    }
}
