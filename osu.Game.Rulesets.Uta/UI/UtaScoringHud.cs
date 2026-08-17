// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Diagnostics;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
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
    private bool debugDiagnostics;
    private bool hudVisible = true;
    private double lastToggleTime = double.NegativeInfinity;
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
    private void load(ScoreProcessor scoreProcessor, UtaGameplayScoringController controller, UtaRulesetConfigManager config)
    {
        hudPosition.BindTo(config.GetBindable<UtaScoreHudPosition>(UtaRulesetSetting.ScoreHudPosition));
        hudPosition.BindValueChanged(applyPosition, true);
        debugDiagnostics = config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics).Value;

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
        qualityText.Text = $"综合 {composite.Value:P1}   音程 {pitchAccuracy.Value:P1}   覆盖 {coverage.Value:P1}";
        streakText.Text = $"Combo {nativeCombo.Value:N0}   Accurate {accurateStreak.Value:N0}   {profile.Value}";

        string faults = lastFaults.Value == UtaPitchFault.None ? string.Empty : $" · {formatFaults(lastFaults.Value)}";
        string bias = lastGrade.Value == UtaNoteGrade.Ignored ? string.Empty : $" · {lastBias.Value:+0;-0;0}c";
        noteText.Text = $"当前音符：{lastGrade.Value}{faults}{bias}";
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
        // finishes - repeatedly toggling off an already-fading-out value here would just
        // restart the SAME fade back to back rather than reversing it. Key-repeat while
        // holding S is also ignored so it does not spam the transform queue.
        // Uses a real wall-clock timestamp rather than the gameplay Clock: that clock can jump
        // backwards on seeks/loops/retries, which would make this debounce permanently swallow
        // every subsequent press (CurrentTime - lastToggleTime staying negative forever).
        double now = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
        if (now - lastToggleTime < 150)
            return true;

        lastToggleTime = now;
        hudVisible = !hudVisible;
        this.FadeTo(hudVisible ? 1 : 0, 150, Easing.OutQuint);
        return true;
    }

    public void OnReleased(KeyBindingReleaseEvent<UtaAction> e)
    {
    }

    private static string formatFaults(UtaPitchFault faults)
    {
        var parts = new List<string>();
        if (faults.HasFlag(UtaPitchFault.High)) parts.Add("High");
        if (faults.HasFlag(UtaPitchFault.Low)) parts.Add("Low");
        if (faults.HasFlag(UtaPitchFault.Unstable)) parts.Add("Unstable");
        if (faults.HasFlag(UtaPitchFault.LowCoverage)) parts.Add("Low coverage");
        if (faults.HasFlag(UtaPitchFault.Inaccurate)) parts.Add("Inaccurate");
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
