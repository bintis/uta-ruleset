// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Karaoke.Scoring;

/// <summary>
/// Karaoke is a performance session by default: a singer always reaches the
/// results screen, even when their health reaches zero.
/// </summary>
public partial class KaraokeHealthProcessor : DrainingHealthProcessor
{
    public KaraokeHealthProcessor(double drainStartTime)
        : base(drainStartTime)
    {
    }

    protected override bool CheckDefaultFailCondition(JudgementResult result) => false;
}
