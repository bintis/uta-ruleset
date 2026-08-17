// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Localisation;
using osu.Game.Rulesets.Uta.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Uta-native score HUD. The global lazer score/accuracy counters are hidden by
/// <see cref="osu.Game.Rulesets.Uta.Skinning.UtaHudController"/>, so this panel
/// exposes the continuous vocal score and discrete note feedback during play.
/// Anchored corner is configurable in settings; S toggles it on/off in-game.
/// </summary>
internal sealed partial class UtaScoringHud : CompositeDrawable, IKeyBindingHandler<UtaAction>
{
    private readonly Bindable<UtaScoreHudPosition> hudPosition = new();
    private readonly Bindable<string> locale = new();
    private UtaUiLanguage language = UtaUiLanguage.English;
    private bool debugDiagnostics;
    private bool hudVisible = true;
    private readonly BindableLong totalScore = new();
    private readonly BindableDouble composite = new();
    private readonly BindableDouble pitchAccuracy = new();
    private readonly BindableDouble coverage = new();
    private readonly BindableInt nativeCombo = new();
    private readonly BindableInt accurateStreak = new();
    private readonly Bindable<UtaScoringProfile> profile = new();
    private readonly IBindable<UtaNoteGrade> lastGrade = new Bindable<UtaNoteGrade>();
    private readonly IBindable<UtaPitchFault> lastFaults = new Bindable<UtaPitchFault>();
    private readonly IBindable<int> lastBias = new BindableInt();
    private readonly IBindable<string> archiveStatus = new Bindable<string>();

    private readonly OsuSpriteText scoreText;
    private readonly OsuSpriteText qualityText;
    private readonly OsuSpriteText streakText;
    private readonly OsuSpriteText noteText;
    private readonly OsuSpriteText archiveText;

    public UtaScoringHud()
    {
        Width = 300;
        Height = 132;
        Masking = true;
        CornerRadius = 9;

        // Without this, fading Alpha to 0 makes the drawable "not present", which drops it
        // out of the non-positional input queue entirely - including its own ToggleScoreHud
        // binding. That's the actual root cause of the HUD getting permanently stuck hidden:
        // once faded out, it stops hearing the very key press meant to bring it back.
        AlwaysPresent = true;

        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(10, 12, 22, 230),
            },
            new Box
            {
                RelativeSizeAxes = Axes.Y,
                Width = 4,
                Colour = new Color4(126, 91, 239, 255),
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Left = 16, Right = 12, Top = 10 },
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 3),
                Children = new Drawable[]
                {
                    scoreText = text(21, FontWeight.Bold),
                    qualityText = text(13, FontWeight.SemiBold),
                    streakText = text(12, FontWeight.Regular),
                    noteText = text(12, FontWeight.SemiBold),
                    archiveText = text(10, FontWeight.Regular, 0.66f),
                },
            },
        };
    }

    [BackgroundDependencyLoader]
    private void load(ScoreProcessor scoreProcessor, UtaGameplayScoringController controller, UtaRulesetConfigManager config, FrameworkConfigManager frameworkConfig)
    {
        hudPosition.BindTo(config.GetBindable<UtaScoreHudPosition>(UtaRulesetSetting.ScoreHudPosition));
        hudPosition.BindValueChanged(applyPosition, true);
        debugDiagnostics = config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics).Value;

        locale.BindTo(frameworkConfig.GetBindable<string>(FrameworkSetting.Locale));
        locale.BindValueChanged(value =>
        {
            language = UtaLanguageResolver.FromLocale(value.NewValue);
            refresh();
        }, true);

        if (scoreProcessor is not UtaScoreProcessor utaScore)
        {
            if (debugDiagnostics)
                Logger.Log($"Uta debug scoring hud: ScoreProcessor is {scoreProcessor.GetType().Name}, not UtaScoreProcessor - HUD stays hidden.");
            Hide();
            return;
        }

        if (debugDiagnostics)
            Logger.Log("Uta debug scoring hud: bound to UtaScoreProcessor.");

        totalScore.BindTo(utaScore.TotalScore);
        composite.BindTo(utaScore.CompositeRating);
        pitchAccuracy.BindTo(utaScore.PitchAccuracy);
        coverage.BindTo(utaScore.Coverage);
        nativeCombo.BindTo(utaScore.Combo);
        accurateStreak.BindTo(utaScore.AccurateStreak);
        profile.BindTo(utaScore.FinalProfile);
        lastGrade.BindTo(controller.LastGrade);
        lastFaults.BindTo(controller.LastFaults);
        lastBias.BindTo(controller.LastBiasCents);
        archiveStatus.BindTo(controller.ArchiveStatus);

        totalScore.BindValueChanged(_ => refresh(), true);
        composite.BindValueChanged(_ => refresh());
        pitchAccuracy.BindValueChanged(_ => refresh());
        coverage.BindValueChanged(_ => refresh());
        nativeCombo.BindValueChanged(_ => refresh());
        accurateStreak.BindValueChanged(_ => refresh());
        profile.BindValueChanged(_ => refresh());
        lastGrade.BindValueChanged(_ => refresh());
        lastFaults.BindValueChanged(_ => refresh());
        lastBias.BindValueChanged(_ => refresh());
        archiveStatus.BindValueChanged(_ => refresh());
    }

    private void refresh()
    {
        if (debugDiagnostics)
        {
            Logger.Log($"Uta debug scoring hud: refresh totalScore={totalScore.Value} composite={composite.Value:P1} "
                       + $"combo={nativeCombo.Value} grade={lastGrade.Value}");
        }

        scoreText.Text = $"{totalScore.Value / (double)UtaScoringOptions.MAX_SCORE * 100:0.00} / 100";
        qualityText.Text = $"{UtaStrings.Get("hud.composite", language)} {composite.Value:P1}   "
                            + $"{UtaStrings.Get("hud.pitch", language)} {pitchAccuracy.Value:P1}   "
                            + $"{UtaStrings.Get("hud.coverage", language)} {coverage.Value:P1}";
        streakText.Text = $"{UtaStrings.Get("hud.combo", language)} {nativeCombo.Value:N0}   "
                           + $"{UtaStrings.Get("hud.accurate", language)} {accurateStreak.Value:N0}   {profile.Value}";

        string faults = lastFaults.Value == UtaPitchFault.None ? string.Empty : $" · {formatFaults(lastFaults.Value)}";
        string bias = lastGrade.Value == UtaNoteGrade.Ignored ? string.Empty : $" · {lastBias.Value:+0;-0;0}c";
        noteText.Text = $"{UtaStrings.Get("hud.current_note", language)}：{lastGrade.Value}{faults}{bias}";
        archiveText.Text = archiveStatus.Value;
    }

    private void applyPosition(ValueChangedEvent<UtaScoreHudPosition> position)
    {
        (Anchor anchor, Vector2 offset) = resolvePosition(position.NewValue);
        Anchor = anchor;
        Origin = anchor;
        Position = offset;
    }

    private static (Anchor Anchor, Vector2 Offset) resolvePosition(UtaScoreHudPosition position)
        => position switch
        {
            UtaScoreHudPosition.TopLeft => (Anchor.TopLeft, new Vector2(24, 205)),
            UtaScoreHudPosition.BottomLeft => (Anchor.BottomLeft, new Vector2(24, -24)),
            UtaScoreHudPosition.BottomRight => (Anchor.BottomRight, new Vector2(-24, -24)),
            _ => (Anchor.TopRight, new Vector2(-24, 205)),
        };

    public bool OnPressed(KeyBindingPressEvent<UtaAction> e)
    {
        if (e.Action != UtaAction.ToggleScoreHud)
            return false;

        // Track the intended state explicitly rather than reading Alpha, which can be
        // mid-transform (e.g. 0.4) if S is pressed again before the previous 150ms fade
        // finishes - toggling off an already-fading-out value here reverses the SAME
        // fade rather than restarting it, so two quick presses always end up back where
        // they started. Do NOT add a time-based debounce on top of this: the key-binding
        // container only raises OnPressed once per physical key-down (OS auto-repeat while
        // the key stays held does not generate additional Pressed events), so a manual
        // "swallow presses within N ms" guard has nothing left to protect against - it only
        // ends up eating a second deliberate press that lands inside the first press's fade
        // window, which is exactly what caused the HUD to "randomly" stay hidden.
        hudVisible = !hudVisible;
        this.FadeTo(hudVisible ? 1 : 0, 150, Easing.OutQuint);

        if (debugDiagnostics)
            Logger.Log($"Uta debug scoring hud: toggle pressed, hudVisible={hudVisible}");

        return true;
    }

    public void OnReleased(KeyBindingReleaseEvent<UtaAction> e)
    {
    }

    private string formatFaults(UtaPitchFault faults)
    {
        var parts = new List<string>();
        if (faults.HasFlag(UtaPitchFault.High)) parts.Add(UtaStrings.Get("fault.high", language));
        if (faults.HasFlag(UtaPitchFault.Low)) parts.Add(UtaStrings.Get("fault.low", language));
        if (faults.HasFlag(UtaPitchFault.Unstable)) parts.Add(UtaStrings.Get("fault.unstable", language));
        if (faults.HasFlag(UtaPitchFault.LowCoverage)) parts.Add(UtaStrings.Get("fault.low_coverage", language));
        if (faults.HasFlag(UtaPitchFault.Inaccurate)) parts.Add(UtaStrings.Get("fault.inaccurate", language));
        return string.Join(" / ", parts);
    }

    private static OsuSpriteText text(float size, FontWeight weight, float alpha = 1)
        => new()
        {
            Font = OsuFont.Default.With(size: size, weight: weight),
            Colour = Color4.White,
            Alpha = alpha,
        };

    protected override void Dispose(bool isDisposing)
    {
        hudPosition.UnbindAll();
        locale.UnbindAll();
        totalScore.UnbindAll();
        composite.UnbindAll();
        pitchAccuracy.UnbindAll();
        coverage.UnbindAll();
        nativeCombo.UnbindAll();
        accurateStreak.UnbindAll();
        profile.UnbindAll();
        lastGrade.UnbindAll();
        lastFaults.UnbindAll();
        lastBias.UnbindAll();
        archiveStatus.UnbindAll();
        base.Dispose(isDisposing);
    }
}
