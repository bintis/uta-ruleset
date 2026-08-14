// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

public sealed class UtaModHidePitchGuide : Mod, IApplicableMod
{
    public override string Name => "No Pitch Guide";
    public override string Acronym => "NPG";
    public override LocalisableString Description => "Hide target notes, the live trace, and pitch feedback.";
    public override IconUsage? Icon => FontAwesome.Solid.EyeSlash;
    public override ModType Type => ModType.DifficultyIncrease;
}
