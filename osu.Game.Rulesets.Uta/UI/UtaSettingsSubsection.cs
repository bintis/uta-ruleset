// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.UI;
using osu.Game.Rulesets.Uta.Pitch;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta;

public sealed partial class UtaSettingsSubsection : RulesetSettingsSubsection
{
    protected override LocalisableString Header => "uta!";

    private UtaRulesetConfigManager config = null!;
    private AudioManager audioManager = null!;
    private GameHost host = null!;
    private SettingsButton latencyCalibrationButton = null!;
    private DiagnosticRow inputLevelDiagnostic = null!;
    private DiagnosticRow detectedPitchDiagnostic = null!;
    private DiagnosticRow routeDiagnostic = null!;
    private FillFlowContainer<Drawable> primarySettings = null!;
    private FillFlowContainer<Drawable> microphoneSettings = null!;
    private readonly CancellationTokenSource calibrationCancellation = new();
    private bool latencyCalibrationRunning;
    private Bindable<string> microphoneDevice = null!;
    private UtaAudioRouter? diagnosticRouter;
    private UtaMicrophoneHandler? diagnosticMicrophone;
    private long lastDiagnosticUpdate;

    public UtaSettingsSubsection(UtaRuleset ruleset)
        : base(ruleset)
    {
    }

    [BackgroundDependencyLoader]
    private void load(AudioManager audioManager, GameHost host)
    {
        config = (UtaRulesetConfigManager)Config;
        this.audioManager = audioManager;
        this.host = host;
        microphoneDevice = config.GetBindable<string>(UtaRulesetSetting.MicrophoneDevice);

        if (config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics).Value)
        {
            Logger.Log($"Uta debug settings subsection: loaded with config instance {config.GetHashCode()} - "
                       + $"mic-output='{config.GetBindable<string>(UtaRulesetSetting.MicrophoneOutputDevice).Value}'");
        }

        Children = new Drawable[]
        {
            primarySettings = page(
                new SettingsItemV2(new FormEnumDropdown<UtaLyricsPosition>
                {
                    Caption = "Lyrics position",
                    HintText = "Place lyrics at the top, centre or bottom of the playfield.",
                    Current = config.GetBindable<UtaLyricsPosition>(UtaRulesetSetting.LyricsPosition),
                }),
                new SettingsItemV2(new FormEnumDropdown<UtaLyricsSize>
                {
                    Caption = "Lyrics size",
                    HintText = "Scale the current line, reading text and upcoming line together.",
                    Current = config.GetBindable<UtaLyricsSize>(UtaRulesetSetting.LyricsSize),
                }),
                new SettingsItemV2(new LyricsTypefaceDropdown
                {
                    Caption = "Lyrics font",
                    HintText = "Use one of lazer's bundled typefaces; missing glyphs still use its fallback fonts.",
                    Current = config.GetBindable<UtaLyricsTypeface>(UtaRulesetSetting.LyricsTypeface),
                }),
                new SettingsItemV2(new PitchCurveDisplayDropdown
                {
                    Caption = "Pitch curves",
                    HintText = "Show the song analysis, your detected pitch, both curves, or neither.",
                    Current = config.GetBindable<UtaPitchCurveDisplay>(UtaRulesetSetting.PitchCurveDisplay),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Singing guide trail",
                    HintText = "Show the thicker glowing trail used by the earlier pitch guide.",
                    Current = config.GetBindable<bool>(UtaRulesetSetting.ShowPitchGuideTrail),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Debug performance logging",
                    HintText = "Write Uta frame, memory, microphone and curve metrics to the runtime log every five seconds.",
                    Current = config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics),
                }),
                new UtaImportDiagnosticsView(),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Reduced motion",
                    HintText = "Disable optional singing/scoring particles and minimise non-essential animation.",
                    Current = config.GetBindable<bool>(UtaRulesetSetting.ReducedMotion),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show background video",
                    HintText = "Allow the lazer-native video event included by the imported .utz package.",
                    Current = config.GetBindable<bool>(UtaRulesetSetting.VideoVisible),
                }),
                slider("Video dim", "Additional ruleset video/background dim level.",
                    config.GetBindable<float>(UtaRulesetSetting.VideoDim), value => $"{value:P0}", 0.05f),
                slider("Video blur", "Additional ruleset video/background blur level.",
                    config.GetBindable<float>(UtaRulesetSetting.VideoBlur), value => $"{value:P0}", 0.05f),
                slider("Video offset", "Ruleset-specific correction added to the packaged video offset.",
                    config.GetBindable<float>(UtaRulesetSetting.VideoOffset), value => $"{value:+0;-0;0} ms", 1),
                slider("Particle intensity", "Intensity of optional singing/scoring feedback; zero disables it.",
                    config.GetBindable<float>(UtaRulesetSetting.ParticleIntensity), value => $"{value:P0}", 0.05f),
                new SettingsItemV2(new FormEnumDropdown<UtaScoreHudPosition>
                {
                    Caption = "Score HUD position",
                    HintText = "Corner of the screen the live score panel is anchored to. Press S in-game to hide or show it.",
                    Current = config.GetBindable<UtaScoreHudPosition>(UtaRulesetSetting.ScoreHudPosition),
                }),
                output("BGM output", "Hardware output used by the instrumental track.", config.GetBindable<string>(UtaRulesetSetting.BackgroundMusicOutputDevice)),
                output("Vocals output", "Hardware output used by the original or guide-vocal track.", config.GetBindable<string>(UtaRulesetSetting.OriginalVocalsOutputDevice)),
                new SettingsItemV2(new MicrophoneDropdown
                {
                    Caption = "Microphone",
                    HintText = "The input device used for live pitch detection.",
                    // Items must be assigned before Current - see buildDeviceItems().
                    Items = UtaDeviceItems.Build(microphoneDevice.Value, UtaMicrophoneDevices.Enumerate().Select(device => device.Name)),
                    Current = microphoneDevice,
                }),
                new SettingsButton
                {
                    Text = "Configure microphone",
                    Action = showMicrophoneSettings,
                },
                slider("Background music", "Volume of the instrumental track during Uta gameplay.",
                    config.GetBindable<double>(UtaRulesetSetting.BackgroundMusicVolume), value => $"{value:P0}", 0.05f),
                slider("Original vocals", "Volume of the independently routed guide-vocal or original track.",
                    config.GetBindable<float>(UtaRulesetSetting.OriginalVocalsVolume), value => $"{value:P0}", 0.05f)),
            microphoneSettings = page(
                new SettingsButton
                {
                    Text = "Back to uta! settings",
                    Action = hideMicrophoneSettings,
                },
                output("Microphone monitor output", "Hardware output used for live microphone monitoring.", config.GetBindable<string>(UtaRulesetSetting.MicrophoneOutputDevice)),
                slider("Microphone input gain", "Software gain applied before pitch detection and monitoring.",
                    config.GetBindable<float>(UtaRulesetSetting.MicrophoneInputGain), value => $"{value:0.00}×", 0.05f),
                slider("Pitch sampling interval", "Time between pitch analyses. Lower values update faster but use more CPU.",
                    config.GetBindable<float>(UtaRulesetSetting.PitchSamplingInterval), value => $"{value:0} ms", 1),
                slider("Microphone monitor", "Hear your microphone through the active output. Headphones are recommended.",
                    config.GetBindable<float>(UtaRulesetSetting.MicrophoneMonitorVolume), value => $"{value:P0}", 0.05f),
                slider("Microphone latency", "Positive values compare detected voice with an earlier point in the song.",
                    config.GetBindable<float>(UtaRulesetSetting.MicrophoneLatency), value => $"{value:+0;-0;0} ms", 1),
                latencyCalibrationButton = new SettingsButton
                {
                    Text = "Auto-measure microphone latency",
                    Action = runLatencyCalibration,
                },
                inputLevelDiagnostic = diagnostic("Input level: run auto-measure to sample"),
                detectedPitchDiagnostic = diagnostic("Detected pitch: run auto-measure to sample"),
                routeDiagnostic = diagnostic("Route: unavailable")),
        };

        microphoneSettings.Hide();
    }

    private void showMicrophoneSettings()
    {
        primarySettings.Hide();
        microphoneSettings.Show();
    }

    private void hideMicrophoneSettings()
    {
        stopMicrophoneDiagnostics();
        microphoneSettings.Hide();
        primarySettings.Show();
    }

    private async void runLatencyCalibration()
    {
        if (latencyCalibrationRunning)
            return;

        latencyCalibrationRunning = true;
        latencyCalibrationButton.Text = "Measuring latency: keep the room quiet...";
        UtaLatencyCalibrationResult result;
        startMicrophoneDiagnostics();
        UtaMicrophoneHandler? microphone = diagnosticMicrophone;

        if (microphone == null)
            result = new UtaLatencyCalibrationResult(false, 0, 0, 0, "Microphone input could not be initialised.");
        else
        {
            try
            {
                result = await microphone.CalibrateLatencyAsync(calibrationCancellation.Token);
            }
            catch (Exception ex)
            {
                result = new UtaLatencyCalibrationResult(false, 0, 0, 0, ex.GetBaseException().Message);
            }
            finally
            {
                stopMicrophoneDiagnostics();
            }
        }

        if (calibrationCancellation.IsCancellationRequested)
            return;

        Schedule(() =>
        {
            if (result.Success)
            {
                config.GetBindable<float>(UtaRulesetSetting.MicrophoneLatency).Value = (float)Math.Round(result.LatencyMilliseconds);
                latencyCalibrationButton.Text = $"Measured {result.LatencyMilliseconds:0} ms; click to measure again";
            }
            else
                latencyCalibrationButton.Text = $"Measurement failed: {result.Message}";

            latencyCalibrationRunning = false;
        });
    }

    private void startMicrophoneDiagnostics()
    {
        if (diagnosticMicrophone != null)
            return;

        try
        {
            diagnosticRouter = new UtaAudioRouter();
            diagnosticRouter.Initialise(audioManager);
            diagnosticMicrophone = new UtaMicrophoneHandler(UtaMicrophoneDevices.Resolve(microphoneDevice.Value), diagnosticRouter);
            diagnosticMicrophone.InputGain.BindTo(config.GetBindable<float>(UtaRulesetSetting.MicrophoneInputGain));
            diagnosticMicrophone.PitchSamplingInterval.BindTo(config.GetBindable<float>(UtaRulesetSetting.PitchSamplingInterval));
            diagnosticMicrophone.MonitorVolume.Value = 0;
            diagnosticMicrophone.OutputDevice.BindTo(config.GetBindable<string>(UtaRulesetSetting.MicrophoneOutputDevice));
            diagnosticMicrophone.PitchDetected += onDiagnosticPitch;

            if (!diagnosticMicrophone.Initialize(host))
                throw new InvalidOperationException("Microphone input could not be initialised.");

            diagnosticMicrophone.Enabled.Value = true;
            int outputLatency = diagnosticRouter.GetOutputLatency(diagnosticMicrophone.OutputDevice.Value);
            string input = string.IsNullOrEmpty(microphoneDevice.Value) ? "system default" : microphoneDevice.Value;
            string output = string.IsNullOrEmpty(diagnosticMicrophone.OutputDevice.Value) ? "lazer default" : diagnosticMicrophone.OutputDevice.Value;
            routeDiagnostic.Text = $"Route: {input} -> {output}  output {outputLatency} ms";
            inputLevelDiagnostic.Text = "Input level: waiting for microphone";
            detectedPitchDiagnostic.Text = "Detected pitch: waiting for microphone";
        }
        catch (Exception ex)
        {
            stopMicrophoneDiagnostics();
            inputLevelDiagnostic.Text = $"Microphone unavailable: {ex.GetBaseException().Message}";
            detectedPitchDiagnostic.Text = "Detected pitch: unavailable";
            routeDiagnostic.Text = "Route: unavailable";
        }
    }

    private void onDiagnosticPitch(UtaPitchFrame frame)
    {
        long now = Stopwatch.GetTimestamp();
        if (lastDiagnosticUpdate != 0 && Stopwatch.GetElapsedTime(lastDiagnosticUpdate, now).TotalMilliseconds < 100)
            return;
        lastDiagnosticUpdate = now;

        Schedule(() =>
        {
            if (diagnosticMicrophone == null)
                return;

            double level = frame.Rms > 0 ? 20 * Math.Log10(frame.Rms) : -90;
            inputLevelDiagnostic.Text = $"Input level: {Math.Clamp(level, -90, 0):0.0} dBFS";
            detectedPitchDiagnostic.Text = frame.Hertz is { } hertz
                ? $"Detected pitch: {hertz:0.0} Hz  clarity {frame.Clarity:P0}"
                : $"Detected pitch: none  clarity {frame.Clarity:P0}";
        });
    }

    private void stopMicrophoneDiagnostics()
    {
        UtaMicrophoneHandler? microphone = diagnosticMicrophone;
        diagnosticMicrophone = null;
        if (microphone != null)
        {
            microphone.PitchDetected -= onDiagnosticPitch;
            microphone.InputGain.UnbindAll();
            microphone.PitchSamplingInterval.UnbindAll();
            microphone.OutputDevice.UnbindAll();
            microphone.Dispose();
        }

        diagnosticRouter?.Dispose();
        diagnosticRouter = null;
        lastDiagnosticUpdate = 0;
    }

    protected override void Dispose(bool isDisposing)
    {
        calibrationCancellation.Cancel();
        stopMicrophoneDiagnostics();
        calibrationCancellation.Dispose();
        base.Dispose(isDisposing);
    }

    private static DiagnosticRow diagnostic(string text) => new(text);

    private static FillFlowContainer<Drawable> page(params Drawable[] children) => new()
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FillDirection.Vertical,
        Children = children,
    };

    private static SettingsItemV2 output(LocalisableString caption, LocalisableString hint, osu.Framework.Bindables.Bindable<string> current)
        => new(new AudioOutputDropdown
        {
            Caption = caption,
            HintText = hint,
            // Items must be assigned before Current - see buildDeviceItems().
            Items = UtaDeviceItems.Build(current.Value, UtaAudioDevices.Enumerate().Select(device => device.Name)),
            Current = current,
        });

    private static SettingsItemV2 slider<T>(LocalisableString caption, LocalisableString hint, osu.Framework.Bindables.Bindable<T> current,
                                             System.Func<T, LocalisableString> format, float keyboardStep)
        where T : struct, System.Numerics.INumber<T>, System.Numerics.IMinMaxValue<T>
        => new(new FormSliderBar<T>
        {
            Caption = caption,
            HintText = hint,
            Current = current,
            LabelFormat = format,
            KeyboardStep = keyboardStep,
        });

    private sealed partial class MicrophoneDropdown : FormDropdown<string>
    {
        protected override LocalisableString GenerateItemText(string item)
            => string.IsNullOrEmpty(item) ? "System default" : item;
    }

    private sealed partial class LyricsTypefaceDropdown : FormEnumDropdown<UtaLyricsTypeface>
    {
        protected override LocalisableString GenerateItemText(UtaLyricsTypeface item)
            => item == UtaLyricsTypeface.TorusAlternate ? "Torus Alternate" : item.ToString();
    }

    private sealed partial class PitchCurveDisplayDropdown : FormEnumDropdown<UtaPitchCurveDisplay>
    {
        protected override LocalisableString GenerateItemText(UtaPitchCurveDisplay item)
            => item switch
            {
                UtaPitchCurveDisplay.MyVoice => "My voice",
                UtaPitchCurveDisplay.Both => "Song + my voice",
                _ => item.ToString(),
            };
    }

    private sealed partial class AudioOutputDropdown : FormDropdown<string>
    {
        protected override LocalisableString GenerateItemText(string item)
            => string.IsNullOrEmpty(item) ? "Lazer default" : item;
    }

    private sealed partial class DiagnosticRow : CompositeDrawable
    {
        private readonly OsuSpriteText text;

        public LocalisableString Text
        {
            get => text.Text;
            set => text.Text = value;
        }

        public DiagnosticRow(LocalisableString initialText)
        {
            RelativeSizeAxes = Axes.X;
            Height = 36;
            Margin = new MarginPadding { Vertical = 2 };
            Masking = true;
            CornerRadius = 6;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(39, 36, 48, 255),
                },
                new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 4,
                    Colour = new Color4(126, 91, 239, 255),
                },
                text = new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 16,
                    Text = initialText,
                },
            };
        }
    }
}
