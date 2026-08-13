// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Karaoke.Mods;

public class KaraokeModOriginalVocals : Mod, IApplicableMod
{
    public override string Name => "Original Vocals";
    public override string Acronym => "VOX";
    public override LocalisableString Description => "Sing with the packaged vocal stem, falling back to the original mix when needed. Volume stays adjustable during play.";
    public override IconUsage? Icon => FontAwesome.Solid.Microphone;
    public override ModType Type => ModType.DifficultyReduction;
}
