// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Gates the practice HUD (P): without this mod, P does nothing. The HUD's speed slider does not
/// go through this mod at all - it binds directly to <c>MasterGameplayClockContainer</c>'s own
/// <c>UserPlaybackRate</c>, lazer's built-in live/mid-song playback-rate control (already wired
/// into clock rate, audio tempo, and scoring generically). An early version drove speed through
/// <see cref="IApplicableToRate"/> on this mod instead; that turned out to only be evaluated once
/// at Player start, not continuously, so the HUD's slider had no live effect.
/// </summary>
public sealed class UtaModPractice : Mod, IApplicableMod
{
    public override string Name => "Practice";

    public override string Acronym => "PR";

    public override IconUsage? Icon => FontAwesome.Solid.GraduationCap;

    public override ModType Type => ModType.Fun;

    public override LocalisableString Description
        => "Opens the practice HUD (P): loop points, phrase navigation, and a live pitch-preserving speed control.";
}
