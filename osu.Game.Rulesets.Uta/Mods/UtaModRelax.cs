// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Turns off vocal judgements, score and note-driven health. Uta scores every
/// play by default; Relax opts back out into a free, unscored singing session
/// with pitch feedback only.
/// </summary>
public sealed class UtaModRelax : Mod, IApplicableMod, IApplicableToBeatmap, IApplicableHealthProcessor
{
    public override string Name => "Relax";

    public override string Acronym => "RX";

    public override LocalisableString Description
        => "关闭实时演唱评分、音符判定与生命值结算。只显示音高反馈，不进行打分。";

    public override IconUsage? Icon => FontAwesome.Solid.Couch;

    public override ModType Type => ModType.DifficultyReduction;

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        foreach (UtaNote note in beatmap.HitObjects.OfType<UtaNote>())
            note.ScoringEnabled = false;
    }

    public HealthProcessor CreateHealthProcessor(double drainStartTime) => new UtaPassiveHealthProcessor();
}
