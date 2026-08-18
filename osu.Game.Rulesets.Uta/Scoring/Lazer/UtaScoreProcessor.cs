// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// Native lazer score processor for Uta judgements. This class is supplied as
/// an integration primitive; it should be activated only when drawable notes
/// are populated from the live scoring session.
/// </summary>
public sealed partial class UtaScoreProcessor : ScoreProcessor
{
    public readonly BindableDouble PitchAccuracy = new(1) { MinValue = 0, MaxValue = 1 };
    public readonly BindableDouble CompositeRating = new(1) { MinValue = 0, MaxValue = 1 };
    public readonly BindableDouble Coverage = new(1) { MinValue = 0, MaxValue = 1 };
    public readonly BindableInt AccurateStreak = new();
    public readonly BindableInt HighestAccurateStreak = new();
    public readonly Bindable<UtaScoringProfile> FinalProfile = new(UtaScoringProfile.Faithful);

    private readonly UtaScoringOptions options;
    private long currentMaximumUnits;
    private long currentPitchEarnedUnits;
    private long currentVoicedUnits;
    private long currentFaithfulEarnedUnits;
    private long currentStableEarnedUnits;
    private long currentTechniqueEarnedUnits;
    private long fullMaximumUnits;
    private bool scoringEnabled;
    private IReadOnlyList<HitObject> orderedHitObjects = Array.Empty<HitObject>();

    public double ForcedCompletionTime => orderedHitObjects.Count == 0
        ? 0
        : orderedHitObjects.Max(hitObject => hitObject.GetEndTime()) + 1000;

    public UtaScoreProcessor(Ruleset ruleset, UtaScoringOptions? options = null)
        : base(ruleset)
    {
        this.options = options ?? new UtaScoringOptions();
        this.options.Validate();
    }

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        orderedHitObjects = enumerateRecursively(beatmap.HitObjects).ToArray();
        scoringEnabled = beatmap.HitObjects.OfType<UtaNote>().Any(note => note.ScoringEnabled);
        base.ApplyBeatmap(beatmap);
    }

    public void CompleteRemainingAsMisses()
    {
        for (int i = JudgedHits; i < orderedHitObjects.Count; i++)
        {
            HitObject hitObject = orderedHitObjects[i];
            JudgementResult result = CreateResult(hitObject, hitObject.Judgement);
            result.Type = result.Judgement.MinResult;
            ApplyResult(result);
        }
    }

    private static IEnumerable<HitObject> enumerateRecursively(IEnumerable<HitObject> hitObjects)
    {
        foreach (HitObject hitObject in hitObjects)
        {
            foreach (HitObject nested in enumerateRecursively(hitObject.NestedHitObjects))
                yield return nested;

            yield return hitObject;
        }
    }

    protected override JudgementResult CreateResult(HitObject hitObject, Judgement judgement)
    {
        var result = new UtaJudgementResult(hitObject, judgement);
        if (IsSimulating && hitObject is UtaNote { ScoringEnabled: true } note)
        {
            UtaScoringTarget target = UtaScoringBeatmapAdapter.CreateTarget(note);
            result.PopulatePerfectSimulation(target.Index, UtaScoringMath.MaximumUnits(target, options));
        }

        return result;
    }

    protected override void ApplyScoreChange(JudgementResult result)
    {
        if (result is not UtaJudgementResult uta)
            return;

        currentMaximumUnits = checked(currentMaximumUnits + uta.MaximumUnits);
        currentPitchEarnedUnits = checked(currentPitchEarnedUnits + uta.PitchEarnedUnits);
        currentVoicedUnits = checked(currentVoicedUnits + uta.VoicedUnits);
        currentFaithfulEarnedUnits = checked(currentFaithfulEarnedUnits + uta.FaithfulEarnedUnits);
        currentStableEarnedUnits = checked(currentStableEarnedUnits + uta.StableEarnedUnits);
        currentTechniqueEarnedUnits = checked(currentTechniqueEarnedUnits + uta.TechniqueEarnedUnits);

        uta.AccurateStreakAtJudgement = AccurateStreak.Value;
        uta.HighestAccurateStreakAtJudgement = HighestAccurateStreak.Value;
        if (uta.Grade != UtaNoteGrade.Ignored)
        {
            AccurateStreak.Value = UtaNativeResultMapper.ContinuesAccurateStreak(uta.Grade)
                ? AccurateStreak.Value + 1
                : 0;
            HighestAccurateStreak.Value = Math.Max(HighestAccurateStreak.Value, AccurateStreak.Value);
        }
        uta.AccurateStreakAfterJudgement = AccurateStreak.Value;
        uta.HighestAccurateStreakAfterJudgement = HighestAccurateStreak.Value;
    }

    protected override void RemoveScoreChange(JudgementResult result)
    {
        if (result is not UtaJudgementResult uta)
            return;

        currentMaximumUnits = checked(currentMaximumUnits - uta.MaximumUnits);
        currentPitchEarnedUnits = checked(currentPitchEarnedUnits - uta.PitchEarnedUnits);
        currentVoicedUnits = checked(currentVoicedUnits - uta.VoicedUnits);
        currentFaithfulEarnedUnits = checked(currentFaithfulEarnedUnits - uta.FaithfulEarnedUnits);
        currentStableEarnedUnits = checked(currentStableEarnedUnits - uta.StableEarnedUnits);
        currentTechniqueEarnedUnits = checked(currentTechniqueEarnedUnits - uta.TechniqueEarnedUnits);

        AccurateStreak.Value -= uta.AccurateStreakAfterJudgement - uta.AccurateStreakAtJudgement;
        HighestAccurateStreak.Value -= uta.HighestAccurateStreakAfterJudgement - uta.HighestAccurateStreakAtJudgement;
    }

    protected override double ComputeTotalScore(double comboProgress, double accuracyProgress, double bonusPortion)
    {
        if (!scoringEnabled)
        {
            PitchAccuracy.Value = 0;
            CompositeRating.Value = 0;
            Coverage.Value = 0;
            Accuracy.Value = 0;
            MinimumAccuracy.Value = 0;
            MaximumAccuracy.Value = 0;
            return 0;
        }

        long denominator = fullMaximumUnits > 0 ? fullMaximumUnits : currentMaximumUnits;
        long finalEarned = currentFaithfulEarnedUnits;
        UtaScoringProfile profile = UtaScoringProfile.Faithful;
        if (currentStableEarnedUnits > finalEarned)
        {
            finalEarned = currentStableEarnedUnits;
            profile = UtaScoringProfile.Stable;
        }
        if (currentTechniqueEarnedUnits > finalEarned)
        {
            finalEarned = currentTechniqueEarnedUnits;
            profile = UtaScoringProfile.Technique;
        }

        PitchAccuracy.Value = currentMaximumUnits > 0 ? currentPitchEarnedUnits / (double)currentMaximumUnits : 1;
        Coverage.Value = currentMaximumUnits > 0 ? currentVoicedUnits / (double)currentMaximumUnits : 1;
        CompositeRating.Value = currentMaximumUnits > 0 ? finalEarned / (double)currentMaximumUnits : 1;
        FinalProfile.Value = profile;
        Accuracy.Value = CompositeRating.Value;

        if (denominator > 0)
        {
            MinimumAccuracy.Value = Math.Clamp(finalEarned / (double)denominator, 0, 1);
            long remaining = Math.Max(0, denominator - currentMaximumUnits);
            MaximumAccuracy.Value = Math.Clamp((finalEarned + remaining) / (double)denominator, 0, 1);
            long roundedScore = UtaScoringMath.RoundDivide(
                checked(finalEarned * UtaScoringOptions.MAX_SCORE),
                denominator);
            return Math.Clamp(roundedScore, 0, UtaScoringOptions.MAX_SCORE);
        }

        MinimumAccuracy.Value = 0;
        MaximumAccuracy.Value = 1;
        return 0;
    }

    protected override void Reset(bool storeResults)
    {
        base.Reset(storeResults);

        if (storeResults)
            fullMaximumUnits = currentMaximumUnits;
        else
            fullMaximumUnits = 0;

        currentMaximumUnits = 0;
        currentPitchEarnedUnits = 0;
        currentVoicedUnits = 0;
        currentFaithfulEarnedUnits = 0;
        currentStableEarnedUnits = 0;
        currentTechniqueEarnedUnits = 0;
        double initialRating = scoringEnabled ? 1 : 0;
        PitchAccuracy.Value = initialRating;
        CompositeRating.Value = initialRating;
        Coverage.Value = initialRating;
        Accuracy.Value = initialRating;
        MinimumAccuracy.Value = 0;
        MaximumAccuracy.Value = initialRating;
        AccurateStreak.Value = 0;
        HighestAccurateStreak.Value = 0;
        FinalProfile.Value = UtaScoringProfile.Faithful;
    }

    public override void PopulateScore(ScoreInfo score)
    {
        base.PopulateScore(score);
        score.Accuracy = scoringEnabled ? CompositeRating.Value : 0;
        score.Rank = RankFromScore(score.Accuracy, score.Statistics);
    }
}
