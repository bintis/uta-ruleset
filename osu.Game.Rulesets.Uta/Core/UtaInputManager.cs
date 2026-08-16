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
    private UtaPitchFrame? pendingPitch;
    private bool pitchUpdateScheduled;
    private bool octaveFoldEnabled;
    private bool autoEnabled;
    private readonly BindableFloat microphoneLatency = new();
    private readonly BindableFloat keyShiftSemitones = new();
    private GameplayClockContainer? gameplayClock;

    public UtaInputManager(RulesetInfo ruleset)
        : base(ruleset, 0, SimultaneousBindingMode.All)
    {
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap, UtaAudioSettingsState audioSettings, AudioManager audioManager, UtaAudioRouter audioRouter,
                      IBindable<IReadOnlyList<Mod>> mods, GameplayClockContainer gameplayClock)
    {
        audioRouter.Initialise(audioManager);
        this.beatmap = beatmap;
        this.gameplayClock = gameplayClock;
        octaveFoldEnabled = mods.Value.Any(mod => mod is UtaModOctaveFold);
        autoEnabled = mods.Value.Any(mod => mod is UtaModAutoplay);
        notes = beatmap.HitObjects.OfType<UtaNote>().OrderBy(note => note.StartTime).ToArray();
        microphoneLatency.BindTo(audioSettings.MicrophoneLatency);
        keyShiftSemitones.BindTo(audioSettings.KeyShiftSemitones);
        gameplayClock.OnSeek += onSeek;

        // Auto watches a synthesized perfect feed (see updateAuto) instead of the microphone -
        // skip opening a real device entirely, so Auto also works without one available.
        if (autoEnabled)
            return;

        microphone = new UtaMicrophoneHandler(UtaMicrophoneDevices.Resolve(audioSettings.MicrophoneDevice.Value), audioRouter);
        microphone.InputGain.BindTo(audioSettings.MicrophoneInputGain);
        microphone.MonitorVolume.BindTo(audioSettings.MicrophoneMonitorVolume);
        microphone.OutputDevice.BindTo(audioSettings.MicrophoneOutputDevice);
        microphone.DebugDiagnostics.BindTo(audioSettings.DebugDiagnostics);
        microphone.PitchSamplingInterval.BindTo(audioSettings.PitchSamplingInterval);
        microphone.PitchDetected += onPitchDetected;
        AddHandler(microphone);
    }

    protected override void Update()
    {
        base.Update();
        if (autoEnabled)
            updateAuto(Time.Current);
    }

    // A perfect virtual singer: always exactly on the shifted target pitch (matching how a real
    // singer's own voice would need to move under a key shift) while a note is active, silent
    // otherwise. This lines up with findTargetAt/target.Y's own keyShift handling - see UtaPitchGuide.
    private void updateAuto(double current)
    {
        UtaNote? active = findNoteAt(current);
        if (active?.Midi is { } targetMidi)
        {
            LiveDetectedPitchMidi.Value = targetMidi + MathF.Round(keyShiftSemitones.Value);
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
        }
    }

    // A seek (manual, gap-skip, or an A-B loop repeat) breaks the continuity a smoothed
    // pitch reading and note lookup assume. Drop stale state immediately rather than
    // waiting for the next microphone frame to disagree wildly with the new position.
    private void onSeek()
    {
        smoothedMidi = null;
        LiveVoiceActive.Value = false;
        LivePitchSimilarity.Value = 0;
    }

    private void onPitchDetected(UtaPitchFrame frame)
    {
        // The recording callback runs independently of lazer's update thread.
        // Never enqueue every microphone window: if the update thread stalls,
        // stale pitch samples otherwise grow without bound and prevent recovery.
        lock (pending_pitch_lock)
        {
            pendingPitch = frame;

            if (pitchUpdateScheduled)
                return;

            pitchUpdateScheduled = true;
        }

        Schedule(processLatestPitch);
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

    /// <summary>
    /// The microphone's hardware round-trip, analysis window and scheduling delay are all real
    /// wall-clock quantities - the singer's voice doesn't slow down with playback. At a gameplay
    /// rate other than 1x, that same real-time gap covers more or less gameplay-clock time, so it
    /// must be scaled by the current rate before subtracting from the current gameplay time.
    /// </summary>
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
        microphoneLatency.UnbindAll();
        keyShiftSemitones.UnbindAll();
        base.Dispose(isDisposing);
    }
}
