// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Localisation;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Rulesets.Uta.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Compact Uta-only judgement details placed directly below the pitch HUD.
/// Total score remains owned and rendered by lazer's GameplayScoreCounter.
/// </summary>
internal sealed partial class UtaScoringHud : CompositeDrawable, IKeyBindingHandler<UtaAction>
{
    private readonly Bindable<string> locale = new();
    private readonly BindableDouble pitchAccuracy = new();
    private readonly BindableDouble coverage = new();
    private readonly BindableInt nativeCombo = new();
    private readonly IBindable<UtaNoteGrade> lastGrade = new Bindable<UtaNoteGrade>();
    private readonly IBindable<UtaPitchFault> lastFaults = new Bindable<UtaPitchFault>();
    private readonly IBindable<int> lastBias = new BindableInt();
    private readonly IBindable<UtaPhraseScore?> lastPhrase = new Bindable<UtaPhraseScore?>();

    private readonly UtaTexturedPrimitive background;
    private readonly Sprite accent;
    private readonly OsuSpriteText judgementText;
    private readonly OsuSpriteText qualityText;
    private readonly Sprite feedbackIcon;
    private readonly Sprite faultIcon;
    private UtaVisualStyle style = UtaVisualStyle.Prism();
    private UtaVisualStyleProvider? styleProvider;
    private UtaUiLanguage language = UtaUiLanguage.English;
    private bool refreshPending;
    private bool hudVisible = true;
    private bool layoutAvailable;

    public UtaScoringHud()
    {
        AlwaysPresent = true;
        Masking = true;
        CornerRadius = 0;

        InternalChildren = new Drawable[]
        {
            background = new UtaTexturedPrimitive
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(10, 12, 22, 238),
            },
            accent = new Sprite
            {
                RelativeSizeAxes = Axes.Y,
                Width = 4,
                FillMode = FillMode.Stretch,
                Alpha = 0,
            },
            judgementText = new OsuSpriteText
            {
                X = 14,
                Y = 3,
                Font = OsuFont.Default.With(size: 12, weight: FontWeight.SemiBold),
                Colour = Color4.White,
            },
            qualityText = new OsuSpriteText
            {
                X = 14,
                Y = 20,
                Font = OsuFont.Default.With(size: 10, weight: FontWeight.Regular),
                Colour = new Color4(207, 212, 235, 255),
            },
            feedbackIcon = new Sprite
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -12,
                Size = new osuTK.Vector2(26),
                Alpha = 0,
            },
            faultIcon = new Sprite
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -42,
                Size = new osuTK.Vector2(16),
                Alpha = 0,
            },
        };
    }

    [BackgroundDependencyLoader]
    private void load(ScoreProcessor scoreProcessor, UtaGameplayScoringController controller, FrameworkConfigManager frameworkConfig,
                      UtaVisualStyleProvider styleProvider)
    {
        this.styleProvider = styleProvider;
        styleProvider.StyleChanged += applyStyle;
        applyStyle(styleProvider.Style);
        locale.BindTo(frameworkConfig.GetBindable<string>(FrameworkSetting.Locale));
        locale.BindValueChanged(value =>
        {
            language = UtaLanguageResolver.FromLocale(value.NewValue);
            requestRefresh();
        }, true);

        if (scoreProcessor is not UtaScoreProcessor utaScore)
        {
            Hide();
            return;
        }

        pitchAccuracy.BindTo(utaScore.PitchAccuracy);
        coverage.BindTo(utaScore.Coverage);
        nativeCombo.BindTo(utaScore.Combo);
        lastGrade.BindTo(controller.LastGrade);
        lastFaults.BindTo(controller.LastFaults);
        lastBias.BindTo(controller.LastBiasCents);
        lastPhrase.BindTo(controller.LastPhraseScore);

        pitchAccuracy.BindValueChanged(_ => requestRefresh(), true);
        coverage.BindValueChanged(_ => requestRefresh());
        nativeCombo.BindValueChanged(_ => requestRefresh());
        lastGrade.BindValueChanged(_ => requestRefresh());
        lastFaults.BindValueChanged(_ => requestRefresh());
        lastBias.BindValueChanged(_ => requestRefresh());
        lastPhrase.BindValueChanged(_ => requestRefresh());
    }

    public void ApplyLayout(RectangleF bounds)
    {
        layoutAvailable = bounds != RectangleF.Empty;
        Position = bounds.Location;
        Size = bounds.Size;
        Alpha = layoutAvailable && hudVisible ? 1 : 0;
    }

    private void requestRefresh()
    {
        if (refreshPending)
            return;

        refreshPending = true;
        Schedule(() =>
        {
            refreshPending = false;
            refresh();
        });
    }

    private void applyStyle(UtaVisualStyle value)
    {
        style = value;
        background.Colour = new Color4(value.Pitch.Panel.R, value.Pitch.Panel.G, value.Pitch.Panel.B, 0.93f);
        background.Texture = value.Assets.HudPanel;
        accent.Texture = value.Assets.HudAccent;
        accent.Colour = value.Feedback.Good;
        accent.Alpha = accent.Texture == null ? 0 : 0.9f;
        qualityText.Colour = value.Lyrics.Upcoming;
        refresh();
    }

    private void refresh()
    {
        string faults = lastFaults.Value == UtaPitchFault.None ? string.Empty : $" · {formatFaults(lastFaults.Value)}";
        feedbackIcon.Texture = style.Assets.FeedbackFor(lastGrade.Value);
        feedbackIcon.Alpha = feedbackIcon.Texture == null || lastGrade.Value == UtaNoteGrade.Ignored ? 0 : 0.9f;
        faultIcon.Texture = style.Assets.FaultFor(lastFaults.Value);
        faultIcon.Alpha = faultIcon.Texture == null ? 0 : 0.9f;
        judgementText.Colour = lastGrade.Value switch
        {
            UtaNoteGrade.Perfect => style.Feedback.Perfect,
            UtaNoteGrade.Great => style.Feedback.Great,
            UtaNoteGrade.Good => style.Feedback.Good,
            UtaNoteGrade.Bad => style.Feedback.Bad,
            UtaNoteGrade.Miss => style.Feedback.Miss,
            _ => style.Lyrics.Current,
        };
        string bias = lastGrade.Value == UtaNoteGrade.Ignored ? string.Empty : $" · {lastBias.Value:+0;-0;0}c";
        judgementText.Text = $"{UtaStrings.Get("hud.current_note", language)} {lastGrade.Value}{bias}{faults}   ·   "
                             + $"{UtaStrings.Get("hud.combo", language)} {nativeCombo.Value:N0}";
        if (lastPhrase.Value is UtaPhraseScore phrase)
        {
            qualityText.Text = $"{UtaStrings.Get("hud.phrase", language)} {phrase.PhraseIndex + 1}  "
                               + $"{phrase.OverallPermille / 10.0:0.0}%   ·   "
                               + $"{UtaStrings.Get("hud.pitch", language)} {phrase.PitchAccuracyPermille / 10.0:0.0}%   ·   "
                               + $"{UtaStrings.Get("hud.coverage", language)} {phrase.CoveragePermille / 10.0:0.0}%   ·   "
                               + $"{UtaStrings.Get("hud.stability", language)} {phrase.StabilityPermille / 10.0:0.0}%";
        }
        else
        {
            qualityText.Text = $"{UtaStrings.Get("hud.pitch", language)} {pitchAccuracy.Value:P1}   ·   "
                               + $"{UtaStrings.Get("hud.coverage", language)} {coverage.Value:P1}";
        }
    }

    public bool OnPressed(KeyBindingPressEvent<UtaAction> e)
    {
        if (e.Action != UtaAction.ToggleScoreHud || e.Repeat)
            return false;

        hudVisible = !hudVisible;
        this.FadeTo(layoutAvailable && hudVisible ? 1 : 0, 150, Easing.OutQuint);
        return true;
    }

    public void OnReleased(KeyBindingReleaseEvent<UtaAction> e)
    {
    }

    private string formatFaults(UtaPitchFault faults)
    {
        var parts = new List<string>(3);
        if (faults.HasFlag(UtaPitchFault.High)) parts.Add(UtaStrings.Get("fault.high", language));
        if (faults.HasFlag(UtaPitchFault.Low)) parts.Add(UtaStrings.Get("fault.low", language));
        if (faults.HasFlag(UtaPitchFault.Unstable)) parts.Add(UtaStrings.Get("fault.unstable", language));
        if (faults.HasFlag(UtaPitchFault.LowCoverage)) parts.Add(UtaStrings.Get("fault.low_coverage", language));
        if (faults.HasFlag(UtaPitchFault.Inaccurate)) parts.Add(UtaStrings.Get("fault.inaccurate", language));
        return string.Join(" / ", parts);
    }

    protected override void Dispose(bool isDisposing)
    {
        locale.UnbindAll();
        pitchAccuracy.UnbindAll();
        coverage.UnbindAll();
        nativeCombo.UnbindAll();
        lastGrade.UnbindAll();
        lastFaults.UnbindAll();
        lastBias.UnbindAll();
        lastPhrase.UnbindAll();
        if (styleProvider != null)
            styleProvider.StyleChanged -= applyStyle;
        base.Dispose(isDisposing);
    }
}
