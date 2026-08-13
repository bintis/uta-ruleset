// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Handlers.Microphone;
using osu.Framework.Input.StateChanges.Events;
using osu.Framework.Input.States;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Rulesets.Karaoke.Beatmaps;
using osu.Game.Rulesets.Karaoke.Configuration;
using osu.Game.Rulesets.Karaoke.Integration.Uta;
using osu.Game.Rulesets.Karaoke.Mods;
using osu.Game.Rulesets.Karaoke.Objects;
using osu.Game.Rulesets.Karaoke.Scoring.Uta;
using osu.Game.Rulesets.Karaoke.UI.Components;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.Karaoke;

public partial class KaraokeInputManager : RulesetInputManager<KaraokeScoringAction>
{
    public BindableFloat LivePitchHertz { get; } = new();
    public BindableFloat LiveDetectedPitchMidi { get; } = new(60);
    public BindableFloat LivePitchDeviation { get; } = new();
    public BindableFloat LivePitchSimilarity { get; } = new();
    public BindableBool LiveVoiceActive { get; } = new();

    public KaraokeInputManager(RulesetInfo ruleset)
        : base(ruleset, 0, SimultaneousBindingMode.All)
    {
        UseParentInput = false;
    }

    private IBeatmap beatmap = null!;
    private KaraokeBeatmap? utaBeatmap;
    private Note[] utaNotes = Array.Empty<Note>();
    private KaraokeSessionStatics session = null!;
    private double? smoothedUtaMidi;
    private long lastMicrophonePitchLogTime;
    private UtaMicrophoneHandler? microphoneHandler;
    private Action<Voice>? directVoiceCallback;
    private Voice lastDirectVoice;
    private bool usesDirectVoiceStream;

    [BackgroundDependencyLoader]
    private void load(KaraokeRulesetConfigManager config, IBindable<IReadOnlyList<Mod>> mods, IBindable<WorkingBeatmap> beatmap, KaraokeBeatmap convertedBeatmap, KaraokeSessionStatics session, EditorBeatmap? editorBeatmap)
    {
        this.session = session;
        if (editorBeatmap != null)
        {
            session.SetValue(KaraokeRulesetSession.ScoringStatus, ScoringStatusMode.Edit);
            return;
        }

        this.beatmap = convertedBeatmap;
        utaBeatmap = convertedBeatmap;
        if (utaBeatmap?.UtaPackageId != null)
            utaNotes = utaBeatmap.HitObjects.OfType<Note>().OrderBy(note => note.StartTime).ToArray();

        bool disableMicrophoneDeviceByMod = mods.Value.OfType<IApplicableToMicrophone>().Any(x => !x.MicrophoneEnabled);

        if (disableMicrophoneDeviceByMod)
        {
            session.SetValue(KaraokeRulesetSession.ScoringStatus, ScoringStatusMode.AutoPlay);
            return;
        }

        bool scorable = convertedBeatmap.IsScorable();

        if (!scorable)
        {
            session.SetValue(KaraokeRulesetSession.ScoringStatus, ScoringStatusMode.NotScoring);
            return;
        }

        try
        {
            string selectedDevice = config.Get<string>(KaraokeRulesetSetting.MicrophoneDevice);
            int deviceIndex = UtaMicrophoneDevices.Resolve(selectedDevice);
            Logger.Log($"Selecting microphone '{(string.IsNullOrEmpty(selectedDevice) ? "system default" : selectedDevice)}' at BASS index {deviceIndex}");
            microphoneHandler = new UtaMicrophoneHandler(deviceIndex);
            microphoneHandler.MonitorVolume.BindTo(config.GetBindable<float>(KaraokeRulesetSetting.MicrophoneMonitorVolume));
            microphoneHandler.InputGain.BindTo(config.GetBindable<float>(KaraokeRulesetSetting.MicrophoneInputGain));

            // The framework's custom input queue stops polling while gameplay is
            // paused and does not reliably recover afterwards. The Uta microphone
            // fork exposes the same pitch stream directly; marshal it back onto the
            // drawable scheduler so pause/resume cannot sever live pitch state.
            if (utaBeatmap?.UtaPackageId != null)
            {
                directVoiceCallback = voice => Schedule(() =>
                {
                    Voice previous = lastDirectVoice;
                    lastDirectVoice = voice;
                    processMicrophoneVoice(voice, previous);
                });
                microphoneHandler.VoiceDetected += directVoiceCallback;
                usesDirectVoiceStream = true;
                Logger.Log("Karaoke microphone direct pitch stream enabled for pause/resume resilience.");
            }

            AddHandler(microphoneHandler);

            session.SetValue(KaraokeRulesetSession.ScoringStatus, ScoringStatusMode.Scoring);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Microphone initialize error.");
            // todo : set real error by exception
            session.SetValue(KaraokeRulesetSession.ScoringStatus, ScoringStatusMode.WindowsMicrophonePermissionDeclined);
        }
    }

    protected override InputState CreateInitialState()
        => new KaraokeRulesetInputManagerInputState<KaraokeScoringAction>(base.CreateInitialState());

    public override void HandleInputStateChange(InputStateChangeEvent inputStateChange)
    {
        switch (inputStateChange)
        {
            case ReplayInputHandler.ReplayStateChangeEvent<KaraokeScoringAction> { Input: ReplayInputHandler.ReplayState<KaraokeScoringAction> replayState } replayStateChanged:
            {
                // Deal with replay event
                // Release event should be trigger first
                if (replayStateChanged.ReleasedActions.Any() && !replayState.PressedActions.Any())
                {
                    foreach (var action in replayStateChanged.ReleasedActions)
                        KeyBindingContainer.TriggerReleased(action);
                }

                // If any key pressed, the continuous send press event
                if (replayState.PressedActions.Any())
                {
                    foreach (var action in replayState.PressedActions)
                        KeyBindingContainer.TriggerPressed(action);
                }

                break;
            }

            case MicrophoneVoiceChangeEvent microphoneSoundChange:
            {
                // Deal with realtime microphone event
                if (microphoneSoundChange.State is not IMicrophoneInputState inputState)
                    throw new NotMicrophoneInputStateException();

                var lastVoice = microphoneSoundChange.LastVoice;
                var voice = inputState.Microphone.Voice;

                // The direct stream is authoritative for Uta. Retain this input
                // event path as a fallback for the published microphone package.
                if (usesDirectVoiceStream && utaBeatmap?.UtaPackageId != null)
                    break;

                processMicrophoneVoice(voice, lastVoice);
                break;
            }

            default:
                // Basically should not goes to here
                base.HandleInputStateChange(inputStateChange);
                break;
        }
    }

    private void processMicrophoneVoice(Voice voice, Voice lastVoice)
    {

        if (voice.HasVoice && Environment.TickCount64 - lastMicrophonePitchLogTime >= 2000)
        {
            lastMicrophonePitchLogTime = Environment.TickCount64;
            Logger.Log($"Karaoke received microphone pitch: {voice.Pitch:F1} Hz");
        }

        float detectedHertz = voice.HasVoice ? voice.Pitch : lastVoice.Pitch;
        float scale;
        float similarity = 0;

        if (utaBeatmap?.UtaPackageId != null)
        {
            double userMidi = UtaPitchMath.FrequencyToMidi(detectedHertz);
            Note? activeNote = findUtaNoteAt(Time.Current);
            double displayMidi = smoothUtaMidi(userMidi, voice.HasVoice);

            if (activeNote?.Midi is { } targetMidi)
            {
                similarity = (float)UtaPitchMath.Similarity(
                    UtaPitchMath.MidiToFrequency(targetMidi),
                    detectedHertz,
                    utaBeatmap.UtaOctaveTolerance);

                if (utaBeatmap.UtaOctaveTolerance)
                {
                    userMidi -= Math.Round((userMidi - targetMidi) / 12) * 12;
                    displayMidi -= Math.Round((displayMidi - targetMidi) / 12) * 12;
                }

                session.SetValue(KaraokeRulesetSession.PitchDeviation, (float)(displayMidi - targetMidi));
                LivePitchDeviation.Value = (float)(displayMidi - targetMidi);
            }

            if (voice.HasVoice && double.IsFinite(displayMidi))
            {
                session.SetValue(KaraokeRulesetSession.DetectedPitchMidi, (float)displayMidi);
                LiveDetectedPitchMidi.Value = (float)displayMidi;
            }

            session.SetValue(KaraokeRulesetSession.PitchSimilarity, similarity);
            session.SetValue(KaraokeRulesetSession.VoiceActive, voice.HasVoice);
            LivePitchHertz.Value = voice.HasVoice ? voice.Pitch : 0;
            LivePitchSimilarity.Value = similarity;
            LiveVoiceActive.Value = voice.HasVoice;
            scale = (float)((displayMidi - utaBeatmap.UtaCentreMidi) / 2);
        }
        else
        {
            // Preserve legacy chart positioning.
            scale = beatmap.PitchToScale(detectedHertz) + 5;
        }

        var action = new KaraokeScoringAction
        {
            Scale = scale,
            Hertz = voice.HasVoice ? voice.Pitch : 0,
            Similarity = similarity,
        };

        if (lastVoice.HasVoice && !voice.HasVoice)
            KeyBindingContainer.TriggerReleased(action);
        else
            KeyBindingContainer.TriggerPressed(action);
    }

    private Note? findUtaNoteAt(double time)
    {
        int low = 0;
        int high = utaNotes.Length - 1;

        while (low <= high)
        {
            int middle = (low + high) / 2;
            Note note = utaNotes[middle];
            if (time < note.StartTime)
                high = middle - 1;
            else if (time > note.EndTime)
                low = middle + 1;
            else
                return note;
        }

        return null;
    }

    private double smoothUtaMidi(double midi, bool hasVoice)
    {
        if (!hasVoice || !double.IsFinite(midi))
        {
            smoothedUtaMidi = null;
            return midi;
        }

        // Smooth only the visual trace. Raw Hertz continues to feed the exact Uta
        // scoring algorithm, keeping grades unaffected by presentation filtering.
        if (smoothedUtaMidi == null || Math.Abs(midi - smoothedUtaMidi.Value) > 5.5)
            smoothedUtaMidi = midi;
        else
            smoothedUtaMidi += (midi - smoothedUtaMidi.Value) * 0.32;

        return smoothedUtaMidi.Value;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (microphoneHandler != null && directVoiceCallback != null)
            microphoneHandler.VoiceDetected -= directVoiceCallback;

        base.Dispose(isDisposing);
    }
}

public class KaraokeRulesetInputManagerInputState<T> : RulesetInputManagerInputState<T>, IMicrophoneInputState
    where T : struct
{
    public MicrophoneState Microphone { get; }

    public KaraokeRulesetInputManagerInputState(InputState state)
        : base(state)
    {
        Microphone = new MicrophoneState();
    }
}

public struct KaraokeScoringAction
{
    public float Scale { get; set; }

    public float Hertz { get; set; }

    public float Similarity { get; set; }
}
