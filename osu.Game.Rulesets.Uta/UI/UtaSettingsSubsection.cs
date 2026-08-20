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
using osu.Game.Rulesets.Uta.Localisation;
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
    private SettingsButton resetDisplayButton = null!;
    private SettingsButton resetPlaybackButton = null!;
    private SettingsButton resetMicrophoneButton = null!;
    private readonly Bindable<string> locale = new();
    private UtaUiLanguage language;
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
    private void load(AudioManager audioManager, GameHost host, osu.Framework.Configuration.FrameworkConfigManager frameworkConfig)
    {
        config = (UtaRulesetConfigManager)Config;
        this.audioManager = audioManager;
        this.host = host;
        microphoneDevice = config.GetBindable<string>(UtaRulesetSetting.MicrophoneDevice);
        locale.BindTo(frameworkConfig.GetBindable<string>(osu.Framework.Configuration.FrameworkSetting.Locale));
        locale.BindValueChanged(value =>
        {
            language = UtaLanguageResolver.FromLocale(value.NewValue);
            refreshLocalisedLabels();
        }, true);

        if (config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics).Value)
        {
            Logger.Log($"Uta debug settings subsection: loaded with config instance {config.GetHashCode()} - "
                       + $"mic-output='{config.GetBindable<string>(UtaRulesetSetting.MicrophoneOutputDevice).Value}'");
        }

        Children = new Drawable[]
        {
            primarySettings = page(
                resetDisplayButton = new SettingsButton
                {
                    Action = resetDisplaySettings,
                },
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
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show upcoming lyrics",
                    HintText = "Show the next lyric line when the responsive HUD has room.",
                    Current = config.GetBindable<bool>(UtaRulesetSetting.LyricsShowUpcoming),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show lyric readings",
                    HintText = "Show authored readings or furigana above lyric tokens.",
                    Current = config.GetBindable<bool>(UtaRulesetSetting.LyricsShowReading),
                }),
                slider("Lyrics panel opacity", "Opacity of the protected lyrics readability surface.",
                    config.GetBindable<float>(UtaRulesetSetting.LyricsPanelOpacity), value => $"{value:P0}", 0.05f),
                new SettingsItemV2(new FormEnumDropdown<UtaLyricsProgressStyle>
                {
                    Caption = "Lyrics progress style",
                    HintText = "Show active word progress as an underline, fill or marker.",
                    Current = config.GetBindable<UtaLyricsProgressStyle>(UtaRulesetSetting.LyricsProgressStyle),
                }),
                new SettingsItemV2(new FormEnumDropdown<UtaPitchHudSize>
                {
                    Caption = "Pitch HUD size",
                    HintText = "Choose a constrained pitch panel size without changing its gameplay time window.",
                    Current = config.GetBindable<UtaPitchHudSize>(UtaRulesetSetting.PitchHudSize),
                }),
                slider("Pitch HUD opacity", "Opacity of the pitch panel while preserving critical cues.",
                    config.GetBindable<float>(UtaRulesetSetting.PitchHudOpacity), value => $"{value:P0}", 0.05f),
                new SettingsItemV2(new FormEnumDropdown<UtaPitchHudLayout>
                {
                    Caption = "Pitch HUD layout",
                    HintText = "Use responsive inset bounds or an explicit full-width panel.",
                    Current = config.GetBindable<UtaPitchHudLayout>(UtaRulesetSetting.PitchHudLayout),
                }),
                slider("HUD safe area padding", "Additional inset reserved around uta! gameplay HUD elements.",
                    config.GetBindable<float>(UtaRulesetSetting.HudSafeAreaPadding), value => $"{value:0} px", 1),
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
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Play original vocals",
                    HintText = "Keep the packaged vocal track on across song changes. The VOX mod also turns this on. The slider below is level only.",
                    Current = config.GetBindable<bool>(UtaRulesetSetting.OriginalVocalsEnabled),
                }),
                slider("Original vocals", "Level of the original vocal track. Does not turn the track on by itself.",
                    config.GetBindable<float>(UtaRulesetSetting.OriginalVocalsVolume), value => $"{value:P0}", 0.05f),
                resetPlaybackButton = new SettingsButton
                {
                    Action = resetPlaybackSettings,
                }),
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
                slider("Ear monitor", "Hear your microphone through the active output. Headphones are recommended.",
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
                routeDiagnostic = diagnostic("Route: unavailable"),
                resetMicrophoneButton = new SettingsButton
                {
                    Action = resetMicrophoneSettings,
                }),
        };

        microphoneSettings.Hide();
        refreshLocalisedLabels();
    }

    private void refreshLocalisedLabels()
    {
        if (resetDisplayButton == null)
            return;

        resetDisplayButton.Text = UtaStrings.Get("settings.reset_display", language);
        resetPlaybackButton.Text = UtaStrings.Get("settings.reset_playback", language);
        resetMicrophoneButton.Text = UtaStrings.Get("settings.reset_microphone", language);
    }

    private void resetDisplaySettings()
    {
        config.GetBindable<UtaLyricsPosition>(UtaRulesetSetting.LyricsPosition).Value = UtaLyricsPosition.Bottom;
        config.GetBindable<UtaLyricsSize>(UtaRulesetSetting.LyricsSize).Value = UtaLyricsSize.Normal;
        config.GetBindable<UtaLyricsTypeface>(UtaRulesetSetting.LyricsTypeface).Value = UtaLyricsTypeface.Torus;
        config.GetBindable<bool>(UtaRulesetSetting.LyricsShowUpcoming).Value = true;
        config.GetBindable<bool>(UtaRulesetSetting.LyricsShowReading).Value = true;
        config.GetBindable<float>(UtaRulesetSetting.LyricsPanelOpacity).Value = 0.72f;
        config.GetBindable<UtaLyricsProgressStyle>(UtaRulesetSetting.LyricsProgressStyle).Value = UtaLyricsProgressStyle.Underline;
        config.GetBindable<UtaPitchHudSize>(UtaRulesetSetting.PitchHudSize).Value = UtaPitchHudSize.Normal;
        config.GetBindable<float>(UtaRulesetSetting.PitchHudOpacity).Value = 1;
        config.GetBindable<UtaPitchHudLayout>(UtaRulesetSetting.PitchHudLayout).Value = UtaPitchHudLayout.Auto;
        config.GetBindable<float>(UtaRulesetSetting.HudSafeAreaPadding).Value = 0;
        config.GetBindable<UtaPitchCurveDisplay>(UtaRulesetSetting.PitchCurveDisplay).Value = UtaPitchCurveDisplay.Both;
        config.GetBindable<bool>(UtaRulesetSetting.ShowPitchGuideTrail).Value = false;
        config.GetBindable<bool>(UtaRulesetSetting.ReducedMotion).Value = false;
        config.GetBindable<bool>(UtaRulesetSetting.VideoVisible).Value = true;
        config.GetBindable<float>(UtaRulesetSetting.VideoDim).Value = 0.35f;
        config.GetBindable<float>(UtaRulesetSetting.VideoBlur).Value = 0;
        config.GetBindable<float>(UtaRulesetSetting.VideoOffset).Value = 0;
        config.GetBindable<float>(UtaRulesetSetting.ParticleIntensity).Value = 0.65f;
        config.GetBindable<UtaScoreHudPosition>(UtaRulesetSetting.ScoreHudPosition).Value = UtaScoreHudPosition.TopRight;
    }

    private void resetPlaybackSettings()
    {
        config.GetBindable<double>(UtaRulesetSetting.BackgroundMusicVolume).Value = 1;
        config.GetBindable<float>(UtaRulesetSetting.OriginalVocalsVolume).Value = 0.55f;
        config.GetBindable<bool>(UtaRulesetSetting.OriginalVocalsEnabled).Value = false;
        config.GetBindable<string>(UtaRulesetSetting.BackgroundMusicOutputDevice).Value = string.Empty;
        config.GetBindable<string>(UtaRulesetSetting.OriginalVocalsOutputDevice).Value = string.Empty;
        config.GetBindable<float>(UtaRulesetSetting.AccompanimentLatency).Value = 0;
        config.GetBindable<float>(UtaRulesetSetting.LyricsLatency).Value = 0;
    }

    private void resetMicrophoneSettings()
    {
        config.GetBindable<string>(UtaRulesetSetting.MicrophoneDevice).Value = string.Empty;
        config.GetBindable<string>(UtaRulesetSetting.MicrophoneOutputDevice).Value = string.Empty;
        config.GetBindable<float>(UtaRulesetSetting.MicrophoneInputGain).Value = 1.5f;
        config.GetBindable<float>(UtaRulesetSetting.MicrophoneMonitorVolume).Value = 0.35f;
        config.GetBindable<float>(UtaRulesetSetting.MicrophoneLatency).Value = 0;
        config.GetBindable<float>(UtaRulesetSetting.PitchSamplingInterval).Value = 10;
        config.GetBindable<float>(UtaRulesetSetting.PhraseLoopLeadIn).Value = 750;
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
            diagnosticMicrophone = new UtaMicrophoneHandler(microphoneDevice.Value, diagnosticRouter);
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
        locale.UnbindAll();
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
        })
        {
            ShowRevertToDefaultButton = true,
        };

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
        })
        {
            ShowRevertToDefaultButton = true,
        };

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
