// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Gates the practice HUD (P): without this mod, P does nothing. The HUD's speed slider binds
/// to a Tempo adjustment on osu's TrackBass (<c>UtaAudioSettingsState.PlaybackTempo</c>) so
/// live speed stays pitch-preserving and stacks with Nightcore/Daycore Frequency.
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
