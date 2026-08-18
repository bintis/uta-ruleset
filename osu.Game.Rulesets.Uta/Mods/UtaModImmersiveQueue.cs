// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Continues to the next queued song after briefly showing the completed score.
/// </summary>
public sealed class UtaModImmersiveQueue : Mod, IApplicableMod
{
    public override string Name => "Immersive Queue";
    public override string Acronym => "IQ";
    public override LocalisableString Description => "After showing the result, automatically continue to the next song in the Uta queue.";
    public override IconUsage? Icon => FontAwesome.Solid.Play;
    public override ModType Type => ModType.Fun;
}
