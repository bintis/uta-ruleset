// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Karaoke.Mods;

public class KaraokeModHidePitchGuide : Mod, IApplicableMod
{
    public override string Name => "No Pitch Guide";
    public override string Acronym => "NPG";
    public override LocalisableString Description => "Hide target notes, your live trace and high/low feedback. Scoring remains active.";
    public override IconUsage? Icon => FontAwesome.Solid.EyeSlash;
    public override ModType Type => ModType.DifficultyIncrease;
}
