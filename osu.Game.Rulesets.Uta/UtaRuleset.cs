// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Formats;
using osuTK;

namespace osu.Game.Rulesets.Uta;

/// <summary>
/// A focused osu!lazer ruleset for playing Uta Studio packages.
/// </summary>
public sealed partial class UtaRuleset : Ruleset
{
    public const string SHORT_NAME = "uta";

    public UtaRuleset()
    {
        UtaBeatmapDecoder.Register();
        RulesetInfo.OnlineID = 111;
    }

    public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

    public override string Description => "uta!";

    public override string ShortName => SHORT_NAME;

    public override string PlayingVerb => "Singing";

    public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
        => new DrawableUtaRuleset(this, beatmap, mods);

    public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
        => new UtaBeatmapConverter(beatmap, this);

    public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap)
        => new UtaDifficultyCalculator(RulesetInfo, beatmap);

    public override IRulesetFilterCriteria CreateRulesetFilterCriteria() => new UtaFilterCriteria();

    public override IEnumerable<Mod> GetModsFor(ModType type) => Array.Empty<Mod>();

    public override Drawable CreateIcon() => new UtaRulesetIcon();

    private sealed partial class UtaRulesetIcon : CompositeDrawable
    {
        public UtaRulesetIcon()
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Size = new Vector2(32);
            InternalChild = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Icon = FontAwesome.Solid.Microphone,
            };
        }

        [BackgroundDependencyLoader(true)]
        private void load(OsuGameBase? game, BeatmapManager? beatmapManager, INotificationOverlay? notifications)
            => UtzImportHandler.EnsureRegistered(game, beatmapManager, notifications);
    }
}
