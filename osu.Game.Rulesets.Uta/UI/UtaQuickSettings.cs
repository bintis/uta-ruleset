// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Localisation;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Screens.Play.PlayerSettings;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

public sealed partial class UtaQuickSettingsContainer : CompositeDrawable, IKeyBindingHandler<UtaAction>
{
    private readonly UtaQuickSettingsOverlay overlay;
    private bool debugDiagnostics;

    // Exposed so DrawableUtaRuleset can cache it for UtaVolumeOverlayExtension, a sibling
    // Overlay that needs to suppress lazer's native volume HUD while this panel is open - see
    // the comment on that class for why. Child-dependency caching only reaches descendants of
    // this container, not siblings, hence surfacing the instance instead.
    public UtaQuickSettingsOverlay Overlay => overlay;

    public UtaQuickSettingsContainer()
    {
        RelativeSizeAxes = Axes.Both;
        InternalChild = overlay = new UtaQuickSettingsOverlay();
    }

    [BackgroundDependencyLoader]
    private void load(UtaRulesetConfigManager config)
    {
        debugDiagnostics = config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics).Value;

        if (debugDiagnostics)
            Logger.Log("Uta debug settings: quick settings container loaded.");
    }

    public bool OnPressed(KeyBindingPressEvent<UtaAction> e)
    {
        if (e.Action != UtaAction.OpenSettings)
            return false;

        if (debugDiagnostics)
            Logger.Log($"Uta debug settings: OpenSettings pressed, overlay state was {overlay.State.Value}");

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

    // BlockScreenWideMouse alone did not stop lazer's GlobalScrollAdjustsVolume from also
    // seeing wheel input while this panel was open. Consuming scroll directly here is a
    // second, more direct line of defence against the same problem regardless of exactly
    // which of the base container's block flags that global handler actually respects.
    protected override bool OnScroll(ScrollEvent e) => true;

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
                    new UtaBackgroundSettings(),
                    new UtaPlaybackSettings(),
                    new UtaDeviceDiagnostics(),
                    new UtaDisplaySettings(),
                    new AudioSettings(),
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
    private readonly SettingsDropdown<UtaPitchHudSize> pitchHudSize;
    private readonly PlayerSliderBar<float> pitchHudOpacity;
    private readonly PlayerCheckbox lyricsShowUpcoming;
    private readonly PlayerCheckbox lyricsShowReading;
    private readonly Bindable<string> locale = new();
    private UtaUiLanguage language;

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
            pitchHudSize = new SettingsDropdown<UtaPitchHudSize>
            {
                LabelText = "Pitch HUD size",
                Items = System.Enum.GetValues<UtaPitchHudSize>(),
            },
            pitchHudOpacity = new PlayerSliderBar<float>
            {
                LabelText = "Pitch HUD opacity",
                DisplayAsPercentage = true,
                KeyboardStep = 0.05f,
            },
            lyricsShowUpcoming = new PlayerCheckbox
            {
                LabelText = "Upcoming lyrics",
            },
            lyricsShowReading = new PlayerCheckbox
            {
                LabelText = "Lyric readings",
            },
        };
    }

    [BackgroundDependencyLoader]
    private void load(UtaRulesetConfigManager config, FrameworkConfigManager frameworkConfig)
    {
        locale.BindTo(frameworkConfig.GetBindable<string>(FrameworkSetting.Locale));
        locale.BindValueChanged(value =>
        {
            language = UtaLanguageResolver.FromLocale(value.NewValue);
            refreshLabels();
        }, true);
        lyricsPosition.Current = config.GetBindable<UtaLyricsPosition>(UtaRulesetSetting.LyricsPosition);
        lyricsSize.Current = config.GetBindable<UtaLyricsSize>(UtaRulesetSetting.LyricsSize);
        lyricsTypeface.Current = config.GetBindable<UtaLyricsTypeface>(UtaRulesetSetting.LyricsTypeface);
        pitchCurveDisplay.Current = config.GetBindable<UtaPitchCurveDisplay>(UtaRulesetSetting.PitchCurveDisplay);
        showPitchGuideTrail.Current = config.GetBindable<bool>(UtaRulesetSetting.ShowPitchGuideTrail);
        pitchHudSize.Current = config.GetBindable<UtaPitchHudSize>(UtaRulesetSetting.PitchHudSize);
        pitchHudOpacity.Current = config.GetBindable<float>(UtaRulesetSetting.PitchHudOpacity);
        lyricsShowUpcoming.Current = config.GetBindable<bool>(UtaRulesetSetting.LyricsShowUpcoming);
        lyricsShowReading.Current = config.GetBindable<bool>(UtaRulesetSetting.LyricsShowReading);
        refreshLabels();
    }

    private void refreshLabels()
    {
        lyricsPosition.LabelText = UtaStrings.Get("quick.lyrics_position", language);
        lyricsSize.LabelText = UtaStrings.Get("quick.lyrics_size", language);
        lyricsTypeface.LabelText = UtaStrings.Get("quick.lyrics_font", language);
        pitchCurveDisplay.LabelText = UtaStrings.Get("quick.pitch_curves", language);
        showPitchGuideTrail.LabelText = UtaStrings.Get("quick.guide_trail", language);
        pitchHudSize.LabelText = UtaStrings.Get("quick.pitch_hud_size", language);
        pitchHudOpacity.LabelText = UtaStrings.Get("quick.pitch_hud_opacity", language);
        lyricsShowUpcoming.LabelText = UtaStrings.Get("quick.upcoming_lyrics", language);
        lyricsShowReading.LabelText = UtaStrings.Get("quick.lyric_readings", language);
    }

    protected override void Dispose(bool isDisposing)
    {
        locale.UnbindAll();
        base.Dispose(isDisposing);
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

/// <summary>
/// Just the two osu!-generic display controls worth keeping for Uta: background dim and blur.
/// The rest of native <see cref="VisualSettings"/> (storyboards, beatmap skins/colours, combo
/// colour normalisation) doesn't apply here - there's no combo colouring or storyboard-driven
/// gameplay - so this replaces it wholesale rather than including it and hiding parts of it.
/// </summary>
public sealed partial class UtaBackgroundSettings : PlayerSettingsGroup
{
    private readonly PlayerSliderBar<double> dim;
    private readonly PlayerSliderBar<double> blur;

    public UtaBackgroundSettings()
        : base("Background")
    {
        Children = new Drawable[]
        {
            dim = new PlayerSliderBar<double>
            {
                LabelText = GameplaySettingsStrings.BackgroundDim,
                DisplayAsPercentage = true,
            },
            blur = new PlayerSliderBar<double>
            {
                LabelText = GameplaySettingsStrings.BackgroundBlur,
                DisplayAsPercentage = true,
            },
        };
    }

    [BackgroundDependencyLoader]
    private void load(OsuConfigManager config)
    {
        dim.Current = config.GetBindable<double>(OsuSetting.DimLevel);
        blur.Current = config.GetBindable<double>(OsuSetting.BlurLevel);
    }
}

public sealed partial class UtaPlaybackSettings : PlayerSettingsGroup
{
    private readonly AudioOutputDropdown backgroundMusicOutput;
    private readonly AudioOutputDropdown vocalsOutput;
    private readonly AudioOutputDropdown microphoneMonitorOutput;
    private readonly PlayerSliderBar<double> backgroundMusicVolume;
    private readonly PlayerCheckbox originalVocalsEnabled;
    private readonly PlayerSliderBar<float> originalVocalsVolume;
    private readonly PlayerSliderBar<float> microphoneMonitorVolume;
    private readonly PlayerSliderBar<float> accompanimentLatency;
    private readonly PlayerSliderBar<float> lyricsLatency;
    private readonly Bindable<string> locale = new();
    private UtaUiLanguage language;

    public UtaPlaybackSettings()
        : base("Uta playback")
    {
        Children = new Drawable[]
        {
            backgroundMusicOutput = createOutput("BGM output"),
            vocalsOutput = createOutput("Vocals output"),
            // Previously only reachable from the global uta! settings page, outside gameplay -
            // meaning it could never actually be changed while testing routing live in-game, and
            // silently stayed on "Lazer default" (a different device to whatever BGM/vocals were
            // explicitly set to) no matter what the player thought they'd configured.
            microphoneMonitorOutput = createOutput("Microphone monitor output"),
            backgroundMusicVolume = createSlider<double>("BGM", "Volume of the instrumental track."),
            originalVocalsEnabled = new PlayerCheckbox
            {
                LabelText = "Play original vocals",
                TooltipText = "Keep the packaged vocal track on across song changes. The VOX mod also turns this on.",
            },
            originalVocalsVolume = createSlider<float>("Original vocals", "Level of the original vocal track. Does not turn the track on by itself."),
            microphoneMonitorVolume = createSlider<float>("Ear monitor", "Hear your microphone through the active output. Same control as EAR MONITOR on the volume overlay."),
            accompanimentLatency = createSlider<float>("Accompaniment latency", "Positive values delay the routed accompaniment and vocals.", false, 1),
            lyricsLatency = createSlider<float>("Lyrics latency", "Positive values display lyrics later.", false, 1),
        };
    }

    [BackgroundDependencyLoader]
    private void load(UtaAudioSettingsState audioSettings, FrameworkConfigManager frameworkConfig)
    {
        locale.BindTo(frameworkConfig.GetBindable<string>(FrameworkSetting.Locale));
        locale.BindValueChanged(value =>
        {
            language = UtaLanguageResolver.FromLocale(value.NewValue);
            refreshLabels();
        }, true);
        string[] availableOutputs = UtaAudioDevices.Enumerate().Select(device => device.Name).ToArray();
        backgroundMusicOutput.Items = UtaDeviceItems.Build(audioSettings.BackgroundMusicOutputDevice.Value, availableOutputs);
        vocalsOutput.Items = UtaDeviceItems.Build(audioSettings.OriginalVocalsOutputDevice.Value, availableOutputs);
        microphoneMonitorOutput.Items = UtaDeviceItems.Build(audioSettings.MicrophoneOutputDevice.Value, availableOutputs);

        backgroundMusicVolume.Current = audioSettings.BackgroundMusicVolume;
        originalVocalsEnabled.Current = audioSettings.OriginalVocalsEnabled;
        originalVocalsVolume.Current = audioSettings.OriginalVocalsVolume;
        microphoneMonitorVolume.Current = audioSettings.MicrophoneMonitorVolume;
        accompanimentLatency.Current = audioSettings.AccompanimentLatency;
        lyricsLatency.Current = audioSettings.LyricsLatency;
        backgroundMusicOutput.Current = audioSettings.BackgroundMusicOutputDevice;
        vocalsOutput.Current = audioSettings.OriginalVocalsOutputDevice;

        Logger.Log($"Uta debug playback settings: mic-output before dropdown bind='{audioSettings.MicrophoneOutputDevice.Value}' "
                   + $"items=[{string.Join(", ", microphoneMonitorOutput.Items)}]");
        microphoneMonitorOutput.Current = audioSettings.MicrophoneOutputDevice;
        Logger.Log($"Uta debug playback settings: mic-output after dropdown bind='{audioSettings.MicrophoneOutputDevice.Value}'");

        // Diagnostic only: log every change to the shared mic-output bindable, regardless of
        // which of its several bound consumers (this dropdown, UtaMicrophoneHandler,
        // UtaRecordingRuntime, UtaSettingsSubsection's own config view) causes it, since it's
        // been observed reset to blank by the time the next play session starts and it isn't
        // yet clear which of those writes it.
        audioSettings.MicrophoneOutputDevice.BindValueChanged(value =>
            Logger.Log($"Uta debug playback settings: mic-output changed '{value.OldValue}' -> '{value.NewValue}'"));
        refreshLabels();
    }

    private void refreshLabels()
    {
        backgroundMusicOutput.LabelText = UtaStrings.Get("quick.bgm_output", language);
        vocalsOutput.LabelText = UtaStrings.Get("quick.vocals_output", language);
        microphoneMonitorOutput.LabelText = UtaStrings.Get("quick.monitor_output", language);
        backgroundMusicVolume.LabelText = UtaStrings.Get("quick.bgm", language);
        originalVocalsEnabled.LabelText = UtaStrings.Get("quick.play_original_vocals", language);
        originalVocalsVolume.LabelText = UtaStrings.Get("quick.original_vocals", language);
        microphoneMonitorVolume.LabelText = UtaStrings.Get("quick.ear_monitor", language);
        accompanimentLatency.LabelText = UtaStrings.Get("quick.accompaniment_latency", language);
        lyricsLatency.LabelText = UtaStrings.Get("quick.lyrics_latency", language);
    }

    protected override void Dispose(bool isDisposing)
    {
        locale.UnbindAll();
        base.Dispose(isDisposing);
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
            Items = new[] { string.Empty },
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
