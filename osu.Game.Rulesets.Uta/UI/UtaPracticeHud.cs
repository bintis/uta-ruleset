// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
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
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Localisation;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.PlayerSettings;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Standalone practice HUD: loop/phrase-navigation controls plus a live, pitch-preserving speed
/// slider, previously a group inside <see cref="UtaQuickSettingsOverlay"/>. Only exists when
/// <see cref="UtaModPractice"/> is selected (see <see cref="Core.DrawableUtaRuleset"/>), and P
/// toggles it independently of the full settings panel (now O) so it can be flicked on/off
/// without leaving gameplay. Loop and phrase actions call straight into the cached
/// <see cref="UtaPracticeController"/>, the same instance the configurable shortcuts drive.
/// </summary>
internal sealed partial class UtaPracticeHud : CompositeDrawable, IKeyBindingHandler<UtaAction>
{
    private bool hudVisible;
    private bool debugDiagnostics;
    private readonly Bindable<string> locale = new();
    private UtaUiLanguage language = UtaUiLanguage.English;

    private readonly OsuSpriteText headerText;
    private readonly PlayerSliderBar<double> speedSlider;
    private readonly OsuSpriteText speedValueText;
    private readonly SettingsButton resetSpeed;
    private readonly OsuSpriteText loopStatus;
    private readonly SettingsButton setLoopA;
    private readonly SettingsButton setLoopB;
    private readonly SettingsButton clearLoop;
    private readonly PlayerCheckbox loopCurrentPhrase;
    private readonly SettingsButton previousPhrase;
    private readonly SettingsButton retryPhrase;
    private readonly SettingsButton nextPhrase;

    private UtaPracticeController practiceController = null!;
    private BindableNumber<double> playbackTempo = null!;

    public UtaPracticeHud()
    {
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        Position = new Vector2(24, 205);
        Width = 300;
        AutoSizeAxes = Axes.Y;
        Masking = true;
        CornerRadius = 9;
        Alpha = 0;

        // Same fix as UtaScoringHud: without this, Alpha == 0 makes the drawable "not present"
        // and it drops out of the input queue - including its own TogglePracticeHud binding -
        // so it can never be shown again (or, starting hidden like this, never shown at all).
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
                Colour = new Color4(91, 156, 239, 255),
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Left = 16, Right = 12, Top = 10, Bottom = 10 },
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    headerText = new OsuSpriteText
                    {
                        Font = OsuFont.Default.With(size: 15, weight: FontWeight.Bold),
                        Colour = Color4.White,
                    },
                    speedSlider = new PlayerSliderBar<double>
                    {
                        KeyboardStep = 0.05f,
                        DisplayAsPercentage = true,
                    },
                    speedValueText = statusText(),
                    resetSpeed = createButton(),
                    loopStatus = statusText(),
                    setLoopA = createButton(),
                    setLoopB = createButton(),
                    clearLoop = createButton(),
                    loopCurrentPhrase = new PlayerCheckbox(),
                    previousPhrase = createButton(),
                    retryPhrase = createButton(),
                    nextPhrase = createButton(),
                },
            },
        };
    }

    [BackgroundDependencyLoader]
    private void load(UtaPracticeController practiceController, UtaRulesetConfigManager config, FrameworkConfigManager frameworkConfig, UtaAudioSettingsState audioSettings)
    {
        this.practiceController = practiceController;
        debugDiagnostics = config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics).Value;

        if (debugDiagnostics)
            Logger.Log("Uta debug practice hud: loaded and bound to UtaPracticeController.");

        playbackTempo = audioSettings.PlaybackTempo;
        speedSlider.Current = playbackTempo;
        resetSpeed.Action = () => playbackTempo.SetDefault();

        setLoopA.Action = practiceController.SetLoopPointA;
        setLoopB.Action = practiceController.SetLoopPointB;
        clearLoop.Action = practiceController.ClearLoopPoints;
        loopCurrentPhrase.Current = practiceController.LoopCurrentPhrase;
        previousPhrase.Action = practiceController.GoToPreviousPhrase;
        retryPhrase.Action = practiceController.RetryPhrase;
        nextPhrase.Action = practiceController.GoToNextPhrase;

        locale.BindTo(frameworkConfig.GetBindable<string>(FrameworkSetting.Locale));
        locale.BindValueChanged(value =>
        {
            language = UtaLanguageResolver.FromLocale(value.NewValue);
            applyLanguage();
        }, true);
    }

    private void applyLanguage()
    {
        headerText.Text = UtaStrings.Get("practice.title", language);
        speedSlider.LabelText = UtaStrings.Get("practice.speed", language);
        speedSlider.TooltipText = UtaStrings.Get("practice.speed_tooltip", language);
        resetSpeed.Text = UtaStrings.Get("practice.reset_speed", language);
        setLoopA.Text = UtaStrings.Get("practice.set_loop_a", language);
        setLoopB.Text = UtaStrings.Get("practice.set_loop_b", language);
        clearLoop.Text = UtaStrings.Get("practice.clear_loop", language);
        loopCurrentPhrase.LabelText = UtaStrings.Get("practice.loop_current_phrase", language);
        previousPhrase.Text = UtaStrings.Get("practice.previous_phrase", language);
        retryPhrase.Text = UtaStrings.Get("practice.retry_phrase", language);
        nextPhrase.Text = UtaStrings.Get("practice.next_phrase", language);
    }

    protected override void Update()
    {
        base.Update();

        speedValueText.Text = string.Format(UtaStrings.Get("practice.current_speed", language), playbackTempo.Value * 100);

        string a = practiceController.LoopPointA.Value is { } pointA ? formatTime(pointA) : "-";
        string b = practiceController.LoopPointB.Value is { } pointB ? formatTime(pointB) : "-";
        loopStatus.Text = practiceController.LoopCurrentPhrase.Value
            ? string.Format(UtaStrings.Get("practice.loop_status_looping", language), practiceController.Phrases.Count)
            : string.Format(UtaStrings.Get("practice.loop_status_points", language), a, b);
    }

    public bool OnPressed(KeyBindingPressEvent<UtaAction> e)
    {
        if (e.Action != UtaAction.TogglePracticeHud)
            return false;

        // Same explicit-state toggle as UtaScoringHud: no artificial debounce, since the
        // key-binding container already only raises this once per physical key-down.
        hudVisible = !hudVisible;
        this.FadeTo(hudVisible ? 1 : 0, 150, Easing.OutQuint);

        if (debugDiagnostics)
            Logger.Log($"Uta debug practice hud: toggle pressed, hudVisible={hudVisible}");

        return true;
    }

    public void OnReleased(KeyBindingReleaseEvent<UtaAction> e)
    {
    }

    private static string formatTime(double ms) => TimeSpan.FromMilliseconds(ms).ToString(@"m\:ss\.ff");

    private static SettingsButton createButton() => new();

    private static OsuSpriteText statusText() => new()
    {
        RelativeSizeAxes = Axes.X,
        Height = 16,
        Font = OsuFont.Default.With(size: 12),
        Colour = new Color4(180, 184, 205, 255),
    };

    protected override void Dispose(bool isDisposing)
    {
        locale.UnbindAll();
        base.Dispose(isDisposing);
    }
}
