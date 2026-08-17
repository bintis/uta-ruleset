// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Uta.Scoring;

public sealed class UtaJudgement : Judgement
{
    public override HitResult MaxResult => HitResult.Perfect;

    public override HitResult MinResult => HitResult.Miss;
}

/// <summary>
/// Native no-score judgement for rap, spoken, freestyle and low-confidence
/// targets. lazer validates every applied result against the judgement's
/// minimum/maximum pair, so these notes require an explicit ignored judgement.
/// </summary>
public sealed class UtaIgnoredJudgement : Judgement
{
    public override HitResult MaxResult => HitResult.IgnoreHit;

    public override HitResult MinResult => HitResult.IgnoreMiss;
}
