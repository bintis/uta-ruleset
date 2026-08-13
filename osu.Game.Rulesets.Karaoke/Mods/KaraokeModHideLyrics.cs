// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Karaoke.Mods;

public class KaraokeModHideLyrics : Mod, IApplicableMod
{
    public override string Name => "No Lyrics";
    public override string Acronym => "NL";
    public override LocalisableString Description => "Hide timed lyrics while keeping pitch detection and scoring active.";
    public override IconUsage? Icon => FontAwesome.Solid.AlignLeft;
    public override ModType Type => ModType.DifficultyIncrease;
}
