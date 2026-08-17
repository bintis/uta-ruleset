// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Keeps scoring active (unlike <see cref="UtaModRelax"/>) but never fails the player out,
/// regardless of how the health processor's rating swings.
/// </summary>
public sealed class UtaModNoFail : Mod, IApplicableMod, IApplicableHealthProcessor
{
    public override string Name => "No Fail";

    public override string Acronym => "NF";

    public override LocalisableString Description => "你不会因为演唱质量下降而失败退出。打分照常进行。";

    public override IconUsage? Icon => FontAwesome.Solid.Heart;

    public override ModType Type => ModType.DifficultyReduction;

    public HealthProcessor CreateHealthProcessor(double drainStartTime) => new UtaPassiveHealthProcessor();
}
