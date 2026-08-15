// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

public sealed class UtaModOctaveFold : Mod, IApplicableMod
{
    public override string Name => "Octave Fold";
    public override string Acronym => "OCT";
    public override LocalisableString Description => "Treat pitches separated by whole octaves as equivalent for scoring and pitch display.";
    public override IconUsage? Icon => FontAwesome.Solid.Music;
    public override ModType Type => ModType.DifficultyReduction;
}
