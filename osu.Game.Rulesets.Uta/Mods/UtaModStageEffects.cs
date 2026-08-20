// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Enables the optional, client-only stage-particle backdrop. Its style and intensity are
/// selected from the in-game quick settings panel (O), so a song never gains visual effects
/// unless the player explicitly selects this MOD.
/// </summary>
public sealed class UtaModStageEffects : Mod, IApplicableMod
{
    public override string Name => "Stage effects";

    public override string Acronym => "FX";

    public override IconUsage? Icon => FontAwesome.Solid.Star;

    public override ModType Type => ModType.Fun;

    public override LocalisableString Description
        => "Adds a configurable particle backdrop. Choose Fireflies, Starlight or Confetti from the in-game settings (O).";
}
