// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Uta.Tests;

/// <summary>
/// Player fixture that supplies a real <see cref="UtaBeatmap"/>. osu's default
/// <c>TestBeatmap</c> converts to zero Uta notes, and <c>Player</c> then skips
/// creating <see cref="DrawableUtaRuleset"/>.
/// </summary>
public abstract partial class UtaPlayerTestScene : PlayerTestScene
{
    protected override Ruleset CreatePlayerRuleset() => new UtaRuleset();

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var info = new BeatmapInfo
        {
            Ruleset = ruleset,
            Metadata = new BeatmapMetadata
            {
                Title = "Uta leftover fixture",
                Artist = "Uta",
            },
            Difficulty = new BeatmapDifficulty(),
            BeatmapSet = new BeatmapSetInfo(),
            Length = 8000,
        };
        info.BeatmapSet.Beatmaps.Add(info);

        return new UtaBeatmap
        {
            BeatmapInfo = info,
            HitObjects =
            {
                new UtaNote { StartTime = 500, Duration = 1500, Midi = 60 },
                new UtaNote { StartTime = 2500, Duration = 1500, Midi = 64 },
                new UtaNote { StartTime = 4500, Duration = 1500, Midi = 67 },
            },
        };
    }

    protected DrawableUtaRuleset DrawableUta => (DrawableUtaRuleset)Player.DrawableRuleset;
}
