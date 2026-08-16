// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Uta.Scoring;

public static class UtaNativeResultMapper
{
    public static HitResult ToHitResult(UtaNoteGrade grade)
        => grade switch
        {
            UtaNoteGrade.Ignored => HitResult.IgnoreHit,
            UtaNoteGrade.Perfect => HitResult.Perfect,
            UtaNoteGrade.Great => HitResult.Great,
            UtaNoteGrade.Good => HitResult.Good,
            UtaNoteGrade.Bad => HitResult.Meh,
            _ => HitResult.Miss,
        };

    public static bool ContinuesNativeCombo(UtaNoteGrade grade)
        => grade is UtaNoteGrade.Perfect or UtaNoteGrade.Great or UtaNoteGrade.Good or UtaNoteGrade.Bad;

    public static bool ContinuesAccurateStreak(UtaNoteGrade grade)
        => grade is UtaNoteGrade.Perfect or UtaNoteGrade.Great or UtaNoteGrade.Good;
}
