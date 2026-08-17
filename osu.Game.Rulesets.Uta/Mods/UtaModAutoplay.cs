// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Watches a perfect virtual performance instead of using the microphone. Deliberately not a
/// subclass of the base game's <see cref="ModAutoplay"/>: that mechanism replays recorded
/// keyboard/cursor frames, which has no equivalent here - Uta's only "input" is continuous
/// microphone pitch, not discrete key presses. Instead <see cref="Core.UtaInputManager"/> detects
/// this mod directly and synthesizes a perfectly-pitched feed in place of the real microphone,
/// the same lightweight pattern <see cref="UtaModOriginalVocals"/>/<see cref="UtaModOctaveFold"/>
/// already use.
/// </summary>
public sealed class UtaModAutoplay : Mod, IApplicableMod
{
    public override string Name => "Auto";
    public override string Acronym => "AT";
    public override IconUsage? Icon => FontAwesome.Solid.Robot;
    public override ModType Type => ModType.Fun;
    public override LocalisableString Description => "Watch a perfect virtual performance; the microphone is not used.";
}
