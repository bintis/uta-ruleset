// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Pitch;
using osu.Game.Rulesets.Uta.Recording;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.Core;

public enum UtaAction
{
    [Description("Open Uta settings")]
    OpenSettings,
    [Description("Set loop point A")]
    SetLoopPointA,
    [Description("Set loop point B")]
    SetLoopPointB,
    [Description("Clear loop points")]
    ClearLoopPoints,
    [Description("Previous phrase")]
    PreviousPhrase,
    [Description("Next phrase")]
    NextPhrase,
    [Description("Retry current phrase")]
    RetryPhrase,
    [Description("Toggle current-phrase looping")]
    ToggleCurrentPhraseLoop,
    [Description("Toggle score HUD")]
    ToggleScoreHud,
    [Description("Toggle practice HUD")]
    TogglePracticeHud,
    [Description("Next queued song")]
    ToggleQueueOverlay,
    [Description("Toggle Uta mobile remote")]
    ToggleRemoteOverlay,
    [Description("Toggle Uta song queue")]
    OpenQueueOverlay,
}

public sealed partial class UtaInputManager : RulesetInputManager<UtaAction>
{
    public BindableFloat LiveDetectedPitchMidi { get; } = new(60);
    public BindableFloat LivePitchDeviation { get; } = new();
    public BindableFloat LivePitchSimilarity { get; } = new();
    public BindableBool LiveVoiceActive { get; } = new();
    public BindableFloat LivePitchClarity { get; } = new();
    public BindableFloat LiveInputLevelDb { get; } = new(-90);
    public BindableDouble LiveDetectedPitchTime { get; } = new();

    private UtaBeatmap beatmap = null!;
    private UtaNote[] notes = Array.Empty<UtaNote>();
    private UtaMicrophoneHandler? microphone;
    private double? smoothedMidi;
    private readonly object pending_pitch_lock = new();
    private readonly Action processLatestPitchAction;
    private UtaPitchFrame? pendingPitch;
    private bool pitchUpdateScheduled;
    private bool octaveFoldEnabled;
    private bool autoEnabled;
    private long lastAutoFrameTimestamp;
    private UtaRuntimeModeState? runtimeModes;
    private readonly BindableFloat microphoneLatency = new();
    private readonly BindableFloat keyShiftSemitones = new();
    private readonly Bindable<string> effectiveMonitorOutput = new();
    private UtaAudioSettingsState? audioSettings;
    private GameplayClockContainer? gameplayClock;
    private UtaGameplayScoringController scoringController = null!;
    private UtaRecordingRuntime recordingRuntime = null!;

    public UtaInputManager(RulesetInfo ruleset)
        : base(ruleset, 0, SimultaneousBindingMode.All)
    {
        processLatestPitchAction = processLatestPitch;
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap, UtaAudioSettingsState audioSettings, AudioManager audioManager, UtaAudioRouter audioRouter,
                      IBindable<IReadOnlyList<Mod>> mods, GameplayClockContainer gameplayClock,
                      UtaGameplayScoringController scoringController, UtaRecordingRuntime recordingRuntime,
                      UtaRuntimeModeState runtimeModes)
    {
        audioRouter.Initialise(audioManager);
        this.beatmap = beatmap;
        this.audioSettings = audioSettings;
        this.gameplayClock = gameplayClock;
        this.scoringController = scoringController;
        this.recordingRuntime = recordingRuntime;
        this.runtimeModes = runtimeModes;
        octaveFoldEnabled = runtimeModes.OctaveFoldEnabled.Value;
        runtimeModes.OctaveFoldEnabled.ValueChanged += onOctaveFoldChanged;
        autoEnabled = mods.Value.Any(mod => mod is UtaModAutoplay);
        notes = beatmap.HitObjects.OfType<UtaNote>().OrderBy(note => note.StartTime).ToArray();
        microphoneLatency.BindTo(audioSettings.MicrophoneLatency);
        keyShiftSemitones.BindTo(audioSettings.KeyShiftSemitones);
        gameplayClock.OnSeek += onSeek;

        if (autoEnabled)
            return;

        microphone = new UtaMicrophoneHandler(UtaMicrophoneDevices.Resolve(audioSettings.MicrophoneDevice.Value), audioRouter);
        microphone.InputGain.BindTo(audioSettings.MicrophoneInputGain);
        microphone.MonitorVolume.BindTo(audioSettings.MicrophoneMonitorVolume);
        audioSettings.MicrophoneDevice.ValueChanged += updateEffectiveMonitorOutput;
        audioSettings.MicrophoneOutputDevice.ValueChanged += updateEffectiveMonitorOutput;
        audioSettings.BackgroundMusicOutputDevice.ValueChanged += updateEffectiveMonitorOutput;
        audioSettings.OriginalVocalsOutputDevice.ValueChanged += updateEffectiveMonitorOutput;
        updateEffectiveMonitorOutput();
        microphone.OutputDevice.BindTo(effectiveMonitorOutput);
        microphone.DebugDiagnostics.BindTo(audioSettings.DebugDiagnostics);
        microphone.PitchSamplingInterval.BindTo(audioSettings.PitchSamplingInterval);
        microphone.PcmCaptureSink = recordingRuntime.RecordingEnabled ? recordingRuntime : null;
        microphone.PitchDetected += onPitchDetected;
        AddHandler(microphone);
    }

    private void updateEffectiveMonitorOutput(ValueChangedEvent<string> _)
        => updateEffectiveMonitorOutput();

    private void updateEffectiveMonitorOutput()
    {
        if (audioSettings == null)
            return;

        string requested = audioSettings.MicrophoneOutputDevice.Value;
        string resolved = UtaAudioSettingsState.ResolveSafeMonitorOutput(
            audioSettings.MicrophoneDevice.Value,
            requested,
            audioSettings.BackgroundMusicOutputDevice.Value,
            audioSettings.OriginalVocalsOutputDevice.Value);
        effectiveMonitorOutput.Value = resolved;

        if (!string.Equals(requested, resolved, StringComparison.OrdinalIgnoreCase))
        {
            osu.Framework.Logging.Logger.Log(
                $"Uta repaired capture device used as monitor output: capture='{audioSettings.MicrophoneDevice.Value}' "
                + $"requested='{requested}' effective='{resolved}'.");
        }
    }

    protected override void Update()
    {
        base.Update();
        if (autoEnabled)
            updateAuto(Time.Current);
    }

    private void updateAuto(double current)
    {
        long now = Stopwatch.GetTimestamp();
        if (lastAutoFrameTimestamp != 0
            && Stopwatch.GetElapsedTime(lastAutoFrameTimestamp, now).TotalMilliseconds < UtaAutoplayFrameFactory.FRAME_DURATION_MILLISECONDS)
            return;

        lastAutoFrameTimestamp = now;
        UtaNote? active = findNoteAt(current);
        int semitones = (int)MathF.Round(keyShiftSemitones.Value);
        UtaPitchFrame frame = UtaAutoplayFrameFactory.Create(active, semitones, now);

        // Auto is a formal scoring source, not just a visual shortcut. Feeding the same
        // bounded capture/scoring pipeline makes HUD, results and regression tests agree.
        scoringController.Enqueue(frame);

        if (active?.Midi is { } targetMidi && frame.Hertz != null)
        {
            LiveDetectedPitchMidi.Value = targetMidi + semitones;
            LivePitchDeviation.Value = 0;
            LivePitchSimilarity.Value = 1;
            LiveDetectedPitchTime.Value = current;
            LiveInputLevelDb.Value = -12;
            LivePitchClarity.Value = 1;
            LiveVoiceActive.Value = true;
        }
        else
        {
            LiveVoiceActive.Value = false;
            LivePitchSimilarity.Value = 0;
            LivePitchClarity.Value = 0;
        }
    }

    private void onOctaveFoldChanged(ValueChangedEvent<bool> value)
        => octaveFoldEnabled = value.NewValue;

    private void onSeek()
    {
        smoothedMidi = null;
        lastAutoFrameTimestamp = 0;
        LiveVoiceActive.Value = false;
        LivePitchSimilarity.Value = 0;
    }

    private void onPitchDetected(UtaPitchFrame frame)
    {
        scoringController.Enqueue(frame);

        lock (pending_pitch_lock)
        {
            pendingPitch = frame;

            if (pitchUpdateScheduled)
                return;

            pitchUpdateScheduled = true;
        }

        Schedule(processLatestPitchAction);
    }

    private void processLatestPitch()
    {
        UtaPitchFrame? frame;

        lock (pending_pitch_lock)
        {
            frame = pendingPitch;
            pendingPitch = null;
            pitchUpdateScheduled = false;
        }

        if (frame != null)
            updatePitch(frame.Value);
    }

    private void updatePitch(UtaPitchFrame frame)
    {
        LivePitchClarity.Value = (float)frame.Clarity;
        LiveInputLevelDb.Value = frame.Rms > 0
            ? (float)Math.Clamp(20 * Math.Log10(frame.Rms), -90, 6)
            : -90;

        double? hertz = frame.Hertz;
        if (hertz == null || !UtaPitchMath.IsFinitePitch(hertz.Value))
        {
            smoothedMidi = null;
            LiveVoiceActive.Value = false;
            LivePitchSimilarity.Value = 0;
            return;
        }

        double rawMidi = UtaPitchMath.FrequencyToMidi(hertz.Value);
        smoothedMidi = smoothedMidi == null || Math.Abs(rawMidi - smoothedMidi.Value) > 5.5
            ? rawMidi
            : smoothedMidi.Value + (rawMidi - smoothedMidi.Value) * 0.32;

        double schedulingAge = Stopwatch.GetElapsedTime(frame.ArrivalTimestamp).TotalMilliseconds;
        double realLatencyMs = microphoneLatency.Value + frame.WindowDurationMilliseconds / 2 + schedulingAge;
        double pitchTime = ComputePitchTime(Time.Current, realLatencyMs, Clock.Rate);
        LiveDetectedPitchTime.Value = pitchTime;
        UtaNote? active = findNoteAt(pitchTime);
        double displayMidi = smoothedMidi.Value;
        double similarity = 0;

        if (active?.Midi is { } target)
        {
            target += (int)MathF.Round(keyShiftSemitones.Value);
            similarity = UtaPitchMath.Similarity(UtaPitchMath.MidiToFrequency(target), hertz.Value, octaveFoldEnabled);
            if (octaveFoldEnabled)
                displayMidi -= Math.Round((displayMidi - target) / 12) * 12;
            LivePitchDeviation.Value = (float)(displayMidi - target);
        }

        LiveDetectedPitchMidi.Value = (float)displayMidi;
        LivePitchSimilarity.Value = (float)similarity;
        LiveVoiceActive.Value = true;
    }

    internal static double ComputePitchTime(double gameplayTime, double realLatencyMs, double rate)
        => gameplayTime - realLatencyMs * Math.Abs(rate);

    private UtaNote? findNoteAt(double time)
    {
        int low = 0;
        int high = notes.Length - 1;

        while (low <= high)
        {
            int middle = (low + high) / 2;
            UtaNote note = notes[middle];
            if (time < note.StartTime)
                high = middle - 1;
            else if (time > note.EndTime)
                low = middle + 1;
            else
                return note;
        }

        return null;
    }

    internal Task<UtaLatencyCalibrationResult> CalibrateLatencyAsync(System.Threading.CancellationToken cancellationToken = default)
        => microphone?.CalibrateLatencyAsync(cancellationToken)
           ?? Task.FromResult(new UtaLatencyCalibrationResult(false, 0, 0, 0, "Microphone input is not available."));

    protected override void Dispose(bool isDisposing)
    {
        if (microphone != null)
            microphone.PitchDetected -= onPitchDetected;
        if (gameplayClock != null)
            gameplayClock.OnSeek -= onSeek;
        if (runtimeModes != null)
            runtimeModes.OctaveFoldEnabled.ValueChanged -= onOctaveFoldChanged;
        if (audioSettings != null)
        {
            audioSettings.MicrophoneDevice.ValueChanged -= updateEffectiveMonitorOutput;
            audioSettings.MicrophoneOutputDevice.ValueChanged -= updateEffectiveMonitorOutput;
            audioSettings.BackgroundMusicOutputDevice.ValueChanged -= updateEffectiveMonitorOutput;
            audioSettings.OriginalVocalsOutputDevice.ValueChanged -= updateEffectiveMonitorOutput;
        }
        microphoneLatency.UnbindAll();
        keyShiftSemitones.UnbindAll();
        effectiveMonitorOutput.UnbindAll();
        base.Dispose(isDisposing);
    }
}
