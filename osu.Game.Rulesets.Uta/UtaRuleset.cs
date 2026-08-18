// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Localisation;
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
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Recording;
using osu.Game.Rulesets.Uta.Remote;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Rulesets.Uta.Skinning;
using osu.Game.Rulesets.Uta.UI;
using osu.Game.Skinning;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Statistics;
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

    public override ScoreProcessor CreateScoreProcessor() => new UtaScoreProcessor(this);

    public override HealthProcessor CreateHealthProcessor(double drainStartTime) => new UtaScoringModeHealthProcessor();

    public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
        => new UtaBeatmapConverter(beatmap, this);

    public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap)
        => new UtaDifficultyCalculator(RulesetInfo, beatmap);

    public override IRulesetFilterCriteria CreateRulesetFilterCriteria() => new UtaFilterCriteria();

    public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
        => Array.Empty<RulesetBeatmapAttribute>();

    public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0)
        => new[]
        {
            new KeyBinding(InputKey.O, UtaAction.OpenSettings),
            new KeyBinding(InputKey.BracketLeft, UtaAction.SetLoopPointA),
            new KeyBinding(InputKey.BracketRight, UtaAction.SetLoopPointB),
            new KeyBinding(InputKey.BackSlash, UtaAction.ClearLoopPoints),
            new KeyBinding(InputKey.Left, UtaAction.PreviousPhrase),
            new KeyBinding(InputKey.Right, UtaAction.NextPhrase),
            new KeyBinding(InputKey.R, UtaAction.RetryPhrase),
            new KeyBinding(InputKey.L, UtaAction.ToggleCurrentPhraseLoop),
            new KeyBinding(InputKey.S, UtaAction.ToggleScoreHud),
            new KeyBinding(InputKey.P, UtaAction.TogglePracticeHud),
            new KeyBinding(InputKey.N, UtaAction.ToggleQueueOverlay),
            new KeyBinding(InputKey.F8, UtaAction.OpenQueueOverlay),
            new KeyBinding(InputKey.K, UtaAction.ToggleRemoteOverlay),
        };

    public override IEnumerable<HitResult> GetValidHitResults()
        => new[]
        {
            HitResult.Miss,
            HitResult.Meh,
            HitResult.Good,
            HitResult.Great,
            HitResult.Perfect,
            HitResult.IgnoreHit,
            HitResult.IgnoreMiss,
        };

    public override LocalisableString GetDisplayNameForHitResult(HitResult result)
        => result == HitResult.Meh ? "Bad" : base.GetDisplayNameForHitResult(result);

    public override StatisticItem[] CreateStatisticsForScore(ScoreInfo score, IBeatmap playableBeatmap)
        => UtaNativeResultsStatistics.Create(score, playableBeatmap);

    public override IEnumerable<Mod> GetModsFor(ModType type)
        => type switch
        {
            ModType.DifficultyIncrease => new Mod[]
            {
                new UtaModHidePitchGuide(),
                new UtaModHideLyrics(),
                new UtaModNightcore(),
            },
            ModType.DifficultyReduction => new Mod[]
            {
                new UtaModRelax(),
                new UtaModNoFail(),
                new UtaModOriginalVocals(),
                new UtaModOctaveFold(),
                new UtaModDaycore(),
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
            ModType.Fun => new Mod[]
            {
                new UtaModAutoplay(),
                new UtaModRecording(),
                new UtaModPractice(),
                new UtaModImmersiveQueue(),
            },
            _ => Array.Empty<Mod>(),
        };

    public override IRulesetConfigManager CreateConfig(SettingsStore? settings)
    {
        repairCorruptedIntegerSettings(settings);

        var config = new UtaRulesetConfigManager(settings, RulesetInfo);
        var root = config.GetBindable<string>(UtaRulesetSetting.PerformanceRootDirectory);
        UtaPerformanceRootRegistry.SetConfiguredRoot(root.Value);
        root.BindValueChanged(value => UtaPerformanceRootRegistry.SetConfiguredRoot(value.NewValue));
        return config;
    }

    // A now-fixed overload resolution bug in UtaRulesetConfigManager.InitialiseDefaults() previously caused these
    // int-typed settings to be persisted as float-formatted strings (e.g. "0.0"). BindableInt.Parse() cannot read
    // that format, which crashes config load on every subsequent run for anyone who ran the affected build.
    // Drop any surviving non-integer values so they get recreated cleanly from defaults.
    private static void repairCorruptedIntegerSettings(SettingsStore? settings)
    {
        var realm = settings?.Realm;

        if (realm == null)
            return;

        var integerKeys = new HashSet<string>
        {
            nameof(UtaRulesetSetting.RemoteControlPort),
        };

        realm.Write(r =>
        {
            // Realm's LINQ provider can't translate HashSet.Contains(), so filter by ruleset in the query
            // and match keys/parse values against the materialised list instead.
            var candidates = r.All<RealmRulesetSetting>()
                               .Where(s => s.RulesetName == SHORT_NAME)
                               .ToList();

            foreach (var setting in candidates)
            {
                if (!integerKeys.Contains(setting.Key))
                    continue;

                if (!int.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    r.Remove(setting);
            }
        });
    }

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
