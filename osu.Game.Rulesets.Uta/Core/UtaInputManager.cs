// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.ComponentModel;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Pitch;

namespace osu.Game.Rulesets.Uta.Core;

public enum UtaAction
{
    [Description("Open Uta settings")]
    OpenSettings,
}

public sealed partial class UtaInputManager : RulesetInputManager<UtaAction>
{
    public BindableFloat LiveDetectedPitchMidi { get; } = new(60);
    public BindableFloat LivePitchDeviation { get; } = new();
    public BindableFloat LivePitchSimilarity { get; } = new();
    public BindableBool LiveVoiceActive { get; } = new();

    private UtaBeatmap beatmap = null!;
    private UtaNote[] notes = Array.Empty<UtaNote>();
    private UtaMicrophoneHandler? microphone;
    private double? smoothedMidi;
    private readonly object pending_pitch_lock = new();
    private double? pendingPitch;
    private bool pitchUpdateScheduled;

    public UtaInputManager(RulesetInfo ruleset)
        : base(ruleset, 0, SimultaneousBindingMode.All)
    {
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap, UtaAudioSettingsState audioSettings, AudioManager audioManager, UtaAudioRouter audioRouter)
    {
        audioRouter.Initialise(audioManager);
        this.beatmap = beatmap;
        notes = beatmap.HitObjects.OfType<UtaNote>().OrderBy(note => note.StartTime).ToArray();
        microphone = new UtaMicrophoneHandler(UtaMicrophoneDevices.Resolve(audioSettings.MicrophoneDevice.Value), audioRouter);
        microphone.InputGain.BindTo(audioSettings.MicrophoneInputGain);
        microphone.MonitorVolume.BindTo(audioSettings.MicrophoneMonitorVolume);
        microphone.OutputDevice.BindTo(audioSettings.MicrophoneOutputDevice);
        microphone.PitchDetected += onPitchDetected;
        AddHandler(microphone);
    }

    private void onPitchDetected(double? hertz)
    {
        // The recording callback runs independently of lazer's update thread.
        // Never enqueue every microphone window: if the update thread stalls,
        // stale pitch samples otherwise grow without bound and prevent recovery.
        lock (pending_pitch_lock)
        {
            pendingPitch = hertz;

            if (pitchUpdateScheduled)
                return;

            pitchUpdateScheduled = true;
        }

        Schedule(processLatestPitch);
    }

    private void processLatestPitch()
    {
        double? hertz;

        lock (pending_pitch_lock)
        {
            hertz = pendingPitch;
            pitchUpdateScheduled = false;
        }

        updatePitch(hertz);
    }

    private void updatePitch(double? hertz)
    {
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

        UtaNote? active = findNoteAt(Time.Current);
        double displayMidi = smoothedMidi.Value;
        double similarity = 0;

        if (active?.Midi is { } target)
        {
            similarity = UtaPitchMath.Similarity(UtaPitchMath.MidiToFrequency(target), hertz.Value, beatmap.OctaveTolerance);
            if (beatmap.OctaveTolerance)
                displayMidi -= Math.Round((displayMidi - target) / 12) * 12;
            LivePitchDeviation.Value = (float)(displayMidi - target);
        }

        LiveDetectedPitchMidi.Value = (float)displayMidi;
        LivePitchSimilarity.Value = (float)similarity;
        LiveVoiceActive.Value = true;
    }

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

    protected override void Dispose(bool isDisposing)
    {
        if (microphone != null)
            microphone.PitchDetected -= onPitchDetected;
        base.Dispose(isDisposing);
    }
}
