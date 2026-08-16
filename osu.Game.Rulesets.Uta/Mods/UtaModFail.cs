// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Integration primitive for note-driven health. Add this mod to
/// <c>UtaRuleset.GetModsFor()</c> only after live Uta judgements are wired.
/// </summary>
public sealed class UtaModFail : Mod, IApplicableMod, IApplicableHealthProcessor
{
    public override string Name => "Fail";
    public override string Acronym => "FL";
    public override LocalisableString Description => "Fail when completed-note singing quality depletes health.";
    public override IconUsage? Icon => FontAwesome.Solid.Heart;
    public override ModType Type => ModType.DifficultyIncrease;

    public HealthProcessor CreateHealthProcessor(double drainStartTime) => new UtaFailHealthProcessor();
}
