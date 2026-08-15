// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

public sealed class UtaModOriginalVocals : Mod, IApplicableMod
{
    public override string Name => "Original Vocals";
    public override string Acronym => "VOX";
    public override LocalisableString Description => "Enable the packaged original vocal track. It is muted unless this mod is selected.";
    public override IconUsage? Icon => FontAwesome.Solid.Microphone;
    public override ModType Type => ModType.DifficultyReduction;
}
