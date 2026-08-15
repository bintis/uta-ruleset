// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Screens.Play.PlayerSettings;
using osuTK;
using osuTK.Graphics;

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
                    new UtaPlaybackSettings(),
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

public sealed partial class UtaPlaybackSettings : PlayerSettingsGroup
{
    private readonly AudioOutputDropdown backgroundMusicOutput;
    private readonly AudioOutputDropdown vocalsOutput;
    private readonly PlayerSliderBar<double> backgroundMusicVolume;
    private readonly PlayerSliderBar<float> originalVocalsVolume;
    private readonly PlayerSliderBar<float> accompanimentLatency;
    private readonly PlayerSliderBar<float> lyricsLatency;

    public UtaPlaybackSettings()
        : base("Uta playback")
    {
        Children = new Drawable[]
        {
            backgroundMusicOutput = createOutput("BGM output"),
            vocalsOutput = createOutput("Vocals output"),
            backgroundMusicVolume = createSlider<double>("BGM", "Volume of the instrumental track."),
            originalVocalsVolume = createSlider<float>("Original vocals", "Volume of the vocal track enabled by the VOX mod."),
            accompanimentLatency = createSlider<float>("Accompaniment latency", "Positive values delay the routed accompaniment and vocals.", false, 1),
            lyricsLatency = createSlider<float>("Lyrics latency", "Positive values display lyrics later.", false, 1),
        };
    }

    [BackgroundDependencyLoader]
    private void load(UtaAudioSettingsState audioSettings)
    {
        backgroundMusicVolume.Current = audioSettings.BackgroundMusicVolume;
        originalVocalsVolume.Current = audioSettings.OriginalVocalsVolume;
        accompanimentLatency.Current = audioSettings.AccompanimentLatency;
        lyricsLatency.Current = audioSettings.LyricsLatency;
        backgroundMusicOutput.Current = audioSettings.BackgroundMusicOutputDevice;
        vocalsOutput.Current = audioSettings.OriginalVocalsOutputDevice;
    }

    private static PlayerSliderBar<T> createSlider<T>(string label, string tooltip, bool percentage = true, float keyboardStep = 0.05f)
        where T : struct, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
        => new()
        {
            LabelText = label,
            TooltipText = tooltip,
            KeyboardStep = keyboardStep,
            DisplayAsPercentage = percentage,
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

public sealed partial class UtaDeviceDiagnostics : PlayerSettingsGroup
{
    private readonly Box inputLevelFill;
    private readonly OsuSpriteText inputLevelText;
    private readonly OsuSpriteText pitchText;
    private readonly OsuSpriteText latencyText;
    private readonly OsuSpriteText routeText;
    private readonly BindableFloat inputLevelDb = new(-90);
    private readonly BindableFloat detectedPitchMidi = new();
    private readonly BindableFloat pitchClarity = new();
    private readonly BindableBool voiceActive = new();
    private readonly BindableFloat microphoneLatency = new();
    private readonly Bindable<string> microphoneDevice = new();
    private readonly Bindable<string> microphoneOutputDevice = new();
    private UtaAudioRouter audioRouter = null!;
    private int outputLatency;

    public UtaDeviceDiagnostics()
        : base("Device diagnostics")
    {
        Children = new Drawable[]
        {
            inputLevelText = diagnosticText("Input: -90 dBFS"),
            new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 8,
                Masking = true,
                CornerRadius = 4,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(35, 39, 55, 255),
                    },
                    inputLevelFill = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Width = 0,
                        Colour = new Color4(64, 201, 142, 255),
                    },
                },
            },
            pitchText = diagnosticText("Pitch: waiting for microphone"),
            latencyText = diagnosticText("Latency: unavailable"),
            routeText = diagnosticText("Route: system default"),
        };
    }

    [BackgroundDependencyLoader]
    private void load(osu.Game.Rulesets.UI.DrawableRuleset drawableRuleset, UtaAudioSettingsState settings, UtaAudioRouter audioRouter)
    {
        this.audioRouter = audioRouter;
        microphoneLatency.BindTo(settings.MicrophoneLatency);
        microphoneDevice.BindTo(settings.MicrophoneDevice);
        microphoneOutputDevice.BindTo(settings.MicrophoneOutputDevice);
        microphoneOutputDevice.BindValueChanged(_ => refreshOutputLatency(), true);

        if (drawableRuleset is not DrawableUtaRuleset utaRuleset)
            return;

        UtaInputManager input = utaRuleset.KeyBindingInputManager;
        inputLevelDb.BindTo(input.LiveInputLevelDb);
        detectedPitchMidi.BindTo(input.LiveDetectedPitchMidi);
        pitchClarity.BindTo(input.LivePitchClarity);
        voiceActive.BindTo(input.LiveVoiceActive);
    }

    protected override void Update()
    {
        base.Update();
        float level = inputLevelDb.Value;
        inputLevelFill.Width = Math.Clamp((level + 60) / 60, 0, 1);
        inputLevelFill.Colour = level > -6
            ? new Color4(235, 92, 92, 255)
            : level > -18
                ? new Color4(236, 190, 72, 255)
                : new Color4(64, 201, 142, 255);
        inputLevelText.Text = $"Input level: {level:0.0} dBFS";

        if (voiceActive.Value)
        {
            double midi = detectedPitchMidi.Value;
            double hertz = osu.Game.Rulesets.Uta.Pitch.UtaPitchMath.MidiToFrequency(midi);
            pitchText.Text = $"Detected pitch: {midiName(midi)}  {hertz:0.0} Hz  clarity {pitchClarity.Value:P0}";
        }
        else
            pitchText.Text = $"Detected pitch: none  clarity {pitchClarity.Value:P0}";

        latencyText.Text = $"Latency: scoring {microphoneLatency.Value:+0;-0;0} ms  output estimate {outputLatency} ms";
        string input = string.IsNullOrEmpty(microphoneDevice.Value) ? "system default" : microphoneDevice.Value;
        string output = string.IsNullOrEmpty(microphoneOutputDevice.Value) ? "lazer default" : microphoneOutputDevice.Value;
        routeText.Text = $"Route: {input} -> {output}";
    }

    private void refreshOutputLatency()
    {
        try
        {
            outputLatency = audioRouter.GetOutputLatency(microphoneOutputDevice.Value);
        }
        catch
        {
            outputLatency = 0;
        }
    }

    private static OsuSpriteText diagnosticText(string text) => new()
    {
        RelativeSizeAxes = Axes.X,
        Height = 18,
        Text = text,
        Colour = new Color4(205, 208, 225, 255),
    };

    private static string midiName(double midi)
    {
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        int rounded = (int)Math.Round(midi);
        return $"{names[((rounded % 12) + 12) % 12]}{rounded / 12 - 1}";
    }

    protected override void Dispose(bool isDisposing)
    {
        inputLevelDb.UnbindAll();
        detectedPitchMidi.UnbindAll();
        pitchClarity.UnbindAll();
        voiceActive.UnbindAll();
        microphoneLatency.UnbindAll();
        microphoneDevice.UnbindAll();
        microphoneOutputDevice.UnbindAll();
        base.Dispose(isDisposing);
    }
}
