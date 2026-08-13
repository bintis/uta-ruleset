// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Karaoke.Mods;

/// <summary>
/// Opts back into lazer's standard draining-health failure behaviour.
/// Karaoke otherwise always continues through to the results screen.
/// </summary>
public class KaraokeModCanFail : Mod, IApplicableHealthProcessor, IApplicableMod
{
    public override string Name => "Fail";
    public override string Acronym => "FL";
    public override IconUsage? Icon => OsuIcon.ModSuddenDeath;
    public override ModType Type => ModType.DifficultyIncrease;
    public override LocalisableString Description => "Enable lazer's normal health failure. Karaoke continues to the end by default.";

    public HealthProcessor CreateHealthProcessor(double drainStartTime) => new DrainingHealthProcessor(drainStartTime);
}
