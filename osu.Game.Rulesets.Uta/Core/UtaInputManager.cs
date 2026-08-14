// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Pitch;

namespace osu.Game.Rulesets.Uta.Core;

public enum UtaAction
{
    None,
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

    public UtaInputManager(RulesetInfo ruleset)
        : base(ruleset, 0, SimultaneousBindingMode.All)
    {
        UseParentInput = false;
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap)
    {
        this.beatmap = beatmap;
        notes = beatmap.HitObjects.OfType<UtaNote>().OrderBy(note => note.StartTime).ToArray();
        microphone = new UtaMicrophoneHandler();
        microphone.PitchDetected += onPitchDetected;
        AddHandler(microphone);
    }

    private void onPitchDetected(double? hertz)
    {
        Schedule(() => updatePitch(hertz));
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
