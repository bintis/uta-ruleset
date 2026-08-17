// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Uta.Core;

namespace osu.Game.Rulesets.Uta.Scoring;

public sealed partial class UtaPassiveHealthProcessor : HealthProcessor
{
    protected override double GetHealthIncreaseFor(JudgementResult result) => 0;

    protected override bool CheckDefaultFailCondition(JudgementResult result) => false;
}

public sealed partial class UtaScoringModeHealthProcessor : HealthProcessor
{
    private readonly UtaScoringOptions options;
    private long maximumUnits;

    public UtaScoringModeHealthProcessor(UtaScoringOptions? options = null)
    {
        this.options = options ?? new UtaScoringOptions();
        this.options.Validate();
    }

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        maximumUnits = beatmap.HitObjects.OfType<UtaNote>()
                              .Select(note => UtaScoringBeatmapAdapter.CreateTarget(note))
                              .Sum(target => UtaScoringMath.MaximumUnits(target, options));
        base.ApplyBeatmap(beatmap);
    }

    protected override double GetHealthIncreaseFor(JudgementResult result)
    {
        if (result is not UtaJudgementResult uta || maximumUnits <= 0 || uta.MaximumUnits <= 0)
            return 0;

        const double neutral_quality = 0.72;
        const double scale = 4.0;
        double noteShare = uta.MaximumUnits / (double)maximumUnits;
        double quality = uta.FinalRatingPermille / (double)UtaScoringOptions.QUALITY_SCALE;
        return Math.Clamp(noteShare * scale * (quality - neutral_quality), -0.2, 0.08);
    }
}
