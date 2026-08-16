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
