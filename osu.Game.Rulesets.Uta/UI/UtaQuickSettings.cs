// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Screens.Play.PlayerSettings;
using osuTK;

namespace osu.Game.Rulesets.Uta.UI;

public sealed partial class UtaQuickSettingsContainer : CompositeDrawable, IKeyBindingHandler<UtaAction>
{
    private readonly UtaQuickSettingsOverlay overlay;

    public UtaQuickSettingsContainer()
    {
        RelativeSizeAxes = Axes.Both;
        InternalChild = overlay = new UtaQuickSettingsOverlay();
    }

    public bool OnPressed(KeyBindingPressEvent<UtaAction> e)
    {
        if (e.Action != UtaAction.OpenSettings)
            return false;

        overlay.ToggleVisibility();
        return true;
    }

    public void OnReleased(KeyBindingReleaseEvent<UtaAction> e)
    {
    }
}

public sealed partial class UtaQuickSettingsOverlay : OsuFocusedOverlayContainer, IKeyBindingHandler<UtaAction>
{
    private const float padding = 20;

    protected override bool DimMainContent => false;

    // Keep gameplay's global scroll-to-volume handler from seeing wheel input
    // while this panel is open, including input outside the panel's own width.
    public override bool BlockScreenWideMouse => true;

    protected override Container<Drawable> Content => groups;

    private readonly FillFlowContainer<Drawable> groups;

    public UtaQuickSettingsOverlay()
    {
        Anchor = Anchor.TopRight;
        Origin = Anchor.TopRight;
        RelativeSizeAxes = Axes.Y;
        Width = SettingsToolboxGroup.CONTAINER_WIDTH + padding * 2;

        InternalChild = new OsuScrollContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = groups = new FillFlowContainer<Drawable>
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 20),
                Padding = new MarginPadding(padding),
                Children = new Drawable[]
                {
                    new VisualSettings(),
                    new UtaDisplaySettings(),
                    new AudioSettings(),
                    new InputSettings(),
                    new UtaAudioSettings(),
                },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        OverlayActivationMode.UnbindAll();
        ((Bindable<OverlayActivation>)OverlayActivationMode).Value = OverlayActivation.All;
    }

    public bool OnPressed(KeyBindingPressEvent<UtaAction> e)
    {
        if (e.Action != UtaAction.OpenSettings || State.Value != Visibility.Visible)
            return false;

        Hide();
        return true;
    }

    public void OnReleased(KeyBindingReleaseEvent<UtaAction> e)
    {
    }

    protected override void PopIn()
    {
        this.MoveToX(0, 400, Easing.OutQuint);
        this.FadeIn(200, Easing.OutQuint);
    }

    protected override void PopOut()
    {
        base.PopOut();
        this.MoveToX(DrawWidth, 400, Easing.OutQuint);
        this.FadeOut(200, Easing.OutQuint);
    }
}

public sealed partial class UtaDisplaySettings : PlayerSettingsGroup
{
    private readonly SettingsDropdown<UtaLyricsPosition> lyricsPosition;
    private readonly SettingsDropdown<UtaLyricsSize> lyricsSize;
    private readonly LyricsTypefaceDropdown lyricsTypeface;
    private readonly PitchCurveDisplayDropdown pitchCurveDisplay;
    private readonly PlayerCheckbox showPitchGuideTrail;

    public UtaDisplaySettings()
        : base("Uta display")
    {
        Children = new Drawable[]
        {
            lyricsPosition = new SettingsDropdown<UtaLyricsPosition>
            {
                LabelText = "Lyrics position",
                Items = System.Enum.GetValues<UtaLyricsPosition>(),
            },
            lyricsSize = new SettingsDropdown<UtaLyricsSize>
            {
                LabelText = "Lyrics size",
                Items = System.Enum.GetValues<UtaLyricsSize>(),
            },
            lyricsTypeface = new LyricsTypefaceDropdown
            {
                LabelText = "Lyrics font",
                Items = System.Enum.GetValues<UtaLyricsTypeface>(),
            },
            pitchCurveDisplay = new PitchCurveDisplayDropdown
            {
                LabelText = "Pitch curves",
                Items = System.Enum.GetValues<UtaPitchCurveDisplay>(),
            },
            showPitchGuideTrail = new PlayerCheckbox
            {
                LabelText = "Singing guide trail",
            },
        };
    }

    [BackgroundDependencyLoader]
    private void load(UtaRulesetConfigManager config)
    {
        lyricsPosition.Current = config.GetBindable<UtaLyricsPosition>(UtaRulesetSetting.LyricsPosition);
        lyricsSize.Current = config.GetBindable<UtaLyricsSize>(UtaRulesetSetting.LyricsSize);
        lyricsTypeface.Current = config.GetBindable<UtaLyricsTypeface>(UtaRulesetSetting.LyricsTypeface);
        pitchCurveDisplay.Current = config.GetBindable<UtaPitchCurveDisplay>(UtaRulesetSetting.PitchCurveDisplay);
        showPitchGuideTrail.Current = config.GetBindable<bool>(UtaRulesetSetting.ShowPitchGuideTrail);
    }

    private sealed partial class LyricsTypefaceDropdown : SettingsDropdown<UtaLyricsTypeface>
    {
        protected override OsuDropdown<UtaLyricsTypeface> CreateDropdown() => new LyricsTypefaceDropdownControl();

        private sealed partial class LyricsTypefaceDropdownControl : DropdownControl
        {
            protected override LocalisableString GenerateItemText(UtaLyricsTypeface item)
                => item == UtaLyricsTypeface.TorusAlternate ? "Torus Alternate" : item.ToString();
        }
    }

    private sealed partial class PitchCurveDisplayDropdown : SettingsDropdown<UtaPitchCurveDisplay>
    {
        protected override OsuDropdown<UtaPitchCurveDisplay> CreateDropdown() => new PitchCurveDropdownControl();

        private sealed partial class PitchCurveDropdownControl : DropdownControl
        {
            protected override LocalisableString GenerateItemText(UtaPitchCurveDisplay item)
                => item switch
                {
                    UtaPitchCurveDisplay.MyVoice => "My voice",
                    UtaPitchCurveDisplay.Both => "Song + my voice",
                    _ => item.ToString(),
                };
        }
    }
}

public sealed partial class UtaAudioSettings : PlayerSettingsGroup
{
    private readonly AudioOutputDropdown backgroundMusicOutput;
    private readonly AudioOutputDropdown vocalsOutput;
    private readonly AudioOutputDropdown microphoneOutput;
    private readonly PlayerSliderBar<double> backgroundMusicVolume;
    private readonly PlayerSliderBar<float> originalVocalsVolume;
    private readonly PlayerSliderBar<float> microphoneInputGain;
    private readonly PlayerSliderBar<float> microphoneMonitorVolume;

    public UtaAudioSettings()
        : base("Uta audio")
    {
        Children = new Drawable[]
        {
            backgroundMusicOutput = createOutput("BGM output"),
            vocalsOutput = createOutput("Vocals output"),
            microphoneOutput = createOutput("Mic monitor output"),
            backgroundMusicVolume = createSlider<double>("BGM", "Volume of the instrumental track."),
            originalVocalsVolume = createSlider<float>("Original vocals", "Volume of the vocal track enabled by the VOX mod."),
            microphoneInputGain = createSlider<float>("Microphone input gain", "Software gain applied before pitch detection."),
            microphoneMonitorVolume = createSlider<float>("My voice", "Hear your microphone through the active output. Headphones are recommended."),
        };
    }

    [BackgroundDependencyLoader]
    private void load(UtaAudioSettingsState audioSettings)
    {
        backgroundMusicVolume.Current = audioSettings.BackgroundMusicVolume;
        originalVocalsVolume.Current = audioSettings.OriginalVocalsVolume;
        microphoneInputGain.Current = audioSettings.MicrophoneInputGain;
        microphoneMonitorVolume.Current = audioSettings.MicrophoneMonitorVolume;
        backgroundMusicOutput.Current = audioSettings.BackgroundMusicOutputDevice;
        vocalsOutput.Current = audioSettings.OriginalVocalsOutputDevice;
        microphoneOutput.Current = audioSettings.MicrophoneOutputDevice;
    }

    private static PlayerSliderBar<T> createSlider<T>(string label, string tooltip)
        where T : struct, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
        => new()
        {
            LabelText = label,
            TooltipText = tooltip,
            KeyboardStep = 0.05f,
            DisplayAsPercentage = true,
        };

    private static AudioOutputDropdown createOutput(string label)
        => new()
        {
            LabelText = label,
            Items = new[] { string.Empty }.Concat(UtaAudioDevices.Enumerate().Select(device => device.Name)).Distinct(),
        };

    private sealed partial class AudioOutputDropdown : SettingsDropdown<string>
    {
        protected override OsuDropdown<string> CreateDropdown() => new OutputDropdownControl();

        private sealed partial class OutputDropdownControl : DropdownControl
        {
            protected override LocalisableString GenerateItemText(string item)
                => string.IsNullOrEmpty(item) ? "Lazer default" : item;
        }
    }
}
