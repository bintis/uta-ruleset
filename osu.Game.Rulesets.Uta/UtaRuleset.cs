// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Skinning;
using osu.Game.Skinning;
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
        UtaAudioRouter.LoadBundledFlacPlugin();
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

    public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
        => Array.Empty<RulesetBeatmapAttribute>();

    public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0)
        => new[] { new KeyBinding(InputKey.P, UtaAction.OpenSettings) };

    public override IEnumerable<Mod> GetModsFor(ModType type)
        => type switch
        {
            ModType.DifficultyIncrease => new Mod[]
            {
                new UtaModHidePitchGuide(),
                new UtaModHideLyrics(),
            },
            ModType.DifficultyReduction => new Mod[]
            {
                new UtaModOriginalVocals(),
                new UtaModOctaveFold(),
            },
            ModType.Conversion => new Mod[]
            {
                new MultiMod(new UtaModTranspose[]
                {
                    new UtaModTransposeMinus6(),
                    new UtaModTransposeMinus5(),
                    new UtaModTransposeMinus4(),
                    new UtaModTransposeMinus3(),
                    new UtaModTransposeMinus2(),
                    new UtaModTransposeMinus1(),
                    new UtaModTransposeOriginal(),
                    new UtaModTransposePlus1(),
                    new UtaModTransposePlus2(),
                    new UtaModTransposePlus3(),
                    new UtaModTransposePlus4(),
                    new UtaModTransposePlus5(),
                    new UtaModTransposePlus6(),
                }),
            },
            _ => Array.Empty<Mod>(),
        };

    public override IRulesetConfigManager CreateConfig(SettingsStore? settings)
        => new UtaRulesetConfigManager(settings, RulesetInfo);

    public override RulesetSettingsSubsection CreateSettings() => new UtaSettingsSubsection(this);

    public override ISkin? CreateSkinTransformer(ISkin skin, IBeatmap beatmap) => new UtaSkinTransformer(skin);

    public override Drawable CreateIcon() => new UtaRulesetIcon();

    private sealed partial class UtaRulesetIcon : CompositeDrawable
    {
        public UtaRulesetIcon()
        {
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Size = new Vector2(32);
            InternalChildren = new Drawable[]
            {
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Scale = new Vector2(0.58f),
                    Icon = FontAwesome.Solid.Microphone,
                },
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Icon = FontAwesome.Regular.Circle,
                },
            };
        }

        [BackgroundDependencyLoader(true)]
        private void load(OsuGameBase? game, BeatmapManager? beatmapManager, INotificationOverlay? notifications)
            => UtzImportHandler.EnsureRegistered(game, beatmapManager, notifications);
    }
}
