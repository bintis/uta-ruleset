// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Karaoke.Scoring;

internal partial class KaraokeScoreProcessor : ScoreProcessor
{
    public KaraokeScoreProcessor()
        : base(new KaraokeRuleset())
    {
    }

    // Karaoke uses a compact, familiar 0–1000 result while retaining lazer's
    // native accuracy/combo processing and results-screen data model.
    protected override double ComputeTotalScore(double comboProgress, double accuracyProgress, double bonusPortion)
        => 1000 * (Accuracy.Value * 0.85 + comboProgress * 0.15 * accuracyProgress);

    public override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
        => accuracy switch
        {
            >= 0.95 => ScoreRank.S,
            >= 0.90 => ScoreRank.A,
            >= 0.80 => ScoreRank.B,
            >= 0.70 => ScoreRank.C,
            _ => ScoreRank.D,
        };
}
