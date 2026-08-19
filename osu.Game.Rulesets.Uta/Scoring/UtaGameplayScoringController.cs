// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Performance;
using osu.Game.Rulesets.Uta.Pitch;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// Authoritative realtime bridge between microphone analysis and the deterministic
/// scorer. Display smoothing stays in <see cref="UtaInputManager"/>; this component
/// owns every formal scoring frame when scoring mode is enabled, and the mapped
/// pitch replay when recording mode is enabled on its own.
/// </summary>
internal sealed partial class UtaGameplayScoringController : Component
{
    private const long watermark_safety_margin_microseconds = 100_000;

    private readonly UtaBeatmap beatmap;
    private readonly UtaScoringTarget[] targets;
    private readonly bool scoringEnabled;
    private readonly bool captureEnabled;
    private readonly UtaCaptureFrameQueue queue = new(4096);
    private readonly UtaGameplayTimelineMapper mapper = new(Stopwatch.Frequency);
    private readonly object replaySync = new();
    private readonly List<UtaPerformancePitchFrame> replayFrames;
    private readonly UtaVocalRangeAdvisor vocalRangeAdvisor = new();
    private readonly Bindable<UtaNoteGrade> lastGrade = new();
    private readonly Bindable<UtaPitchFault> lastFaults = new();
    private readonly BindableInt lastBiasCents = new();
    private readonly Bindable<UtaPhraseScore?> lastPhraseScore = new();
    private readonly Bindable<string> archiveStatus = new();
    private readonly Action<UtaCapturedPitchFrame, UtaScoringFrame> mappedFrameConsumer;
    private readonly List<UtaNoteScore> completedScores = new();

    private GameplayClockContainer gameplayClock = null!;
    private readonly BindableFloat microphoneLatency = new();
    private readonly BindableFloat keyShiftSemitones = new();
    private readonly BindableBool octaveFoldEnabled = new();
    private readonly BindableBool debugDiagnostics = new();
    private volatile bool debugDiagnosticsEnabled;
    private volatile bool forceCompletionRequested;
    private UtaStreamingScoringSession session = null!;
    private UtaScoringOptions options = null!;
    private UtaPerformanceScore? emptyPerformance;
    private double lastRate;
    private bool lastPaused;
    private long lastScoringWatermarkMicroseconds = long.MinValue;
    private bool comparable = true;
    private long diagnosticIntervalStart;
    private int diagnosticEnqueuedFrames;
    private int diagnosticDrainedFrames;
    private int diagnosticAcceptedFrames;
    private int diagnosticEpochMismatches;
    private int diagnosticCompletedNotes;
    private int diagnosticQueryAttempts;
    private int diagnosticQuerySuccesses;
    private int diagnosticNativeApplications;
    private int diagnosticCheckLogSlots;
    private int diagnosticApplyLogSlots;
    private int diagnosticPostEndChecks;
    private int diagnosticCommitDelayPassed;
    private int nextPhraseAnalysisIndex;

    public event Action<UtaNoteScore>? NoteCompleted;

    public bool ScoringEnabled => scoringEnabled;
    public bool CaptureEnabled => captureEnabled;
    public bool Comparable => scoringEnabled && comparable && !queue.Overflowed;
    public bool ForceCompletionRequested => forceCompletionRequested;
    public int TimelineEpoch => mapper.CurrentTimelineEpoch;
    public UtaVocalRangeAdvisor VocalRangeAdvisor => vocalRangeAdvisor;
    public IReadOnlyList<UtaPerformancePitchFrame> ReplayFrames
    {
        get
        {
            lock (replaySync)
                return replayFrames.ToArray();
        }
    }

    public IBindable<UtaNoteGrade> LastGrade => lastGrade;
    public IBindable<UtaPitchFault> LastFaults => lastFaults;
    public IBindable<int> LastBiasCents => lastBiasCents;
    public IBindable<UtaPhraseScore?> LastPhraseScore => lastPhraseScore;
    public IBindable<string> ArchiveStatus => archiveStatus;

    public UtaGameplayScoringController(UtaBeatmap beatmap, bool scoringEnabled, bool captureEnabled)
    {
        this.beatmap = beatmap;
        this.scoringEnabled = scoringEnabled;
        this.captureEnabled = captureEnabled;
        targets = UtaScoringBeatmapAdapter.CreateTargets(beatmap).ToArray();

        // Pitch replay is append-only during a take. Reserving from the chart duration avoids
        // repeated large-array growth/copies in the middle of gameplay. Capacity is capped so
        // malformed/extreme charts cannot force an unreasonable upfront allocation.
        long endMicroseconds = targets.Length == 0 ? 0 : targets.Max(target => target.EndTimeMicroseconds);
        int replayCapacity = captureEnabled
            ? (int)Math.Clamp(endMicroseconds / 10_000 + 64, 256, 65_536)
            : 0;
        replayFrames = new List<UtaPerformancePitchFrame>(replayCapacity);

        archiveStatus.Value = string.Empty;
        mappedFrameConsumer = onMappedFrame;
    }

    public void RequestForceCompletion() => forceCompletionRequested = true;

    [BackgroundDependencyLoader]
    private void load(
        GameplayClockContainer gameplayClock,
        UtaAudioSettingsState audioSettings,
        UtaRuntimeModeState runtimeModes)
    {
        this.gameplayClock = gameplayClock;
        microphoneLatency.BindTo(audioSettings.MicrophoneLatency);
        keyShiftSemitones.BindTo(audioSettings.KeyShiftSemitones);
        octaveFoldEnabled.BindTo(runtimeModes.OctaveFoldEnabled);
        debugDiagnostics.BindTo(audioSettings.DebugDiagnostics);
        debugDiagnostics.BindValueChanged(value =>
        {
            debugDiagnosticsEnabled = value.NewValue;
            diagnosticIntervalStart = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 9 / 4;
        }, true);

        options = new UtaScoringOptions
        {
            TransposeSemitones = (int)MathF.Round(keyShiftSemitones.Value),
            AllowOctaveTolerance = octaveFoldEnabled.Value || beatmap.OctaveTolerance,
            TimelineEpoch = 0,
        };
        session = new UtaStreamingScoringSession(targets, options);
        keyShiftSemitones.BindValueChanged(onTransposeChanged);
        octaveFoldEnabled.BindValueChanged(onOctaveFoldChanged);

        long now = Stopwatch.GetTimestamp();
        lastRate = gameplayClock.Rate;
        lastPaused = gameplayClock.IsPaused.Value;
        mapper.Reset(now, toMicroseconds(gameplayClock.CurrentTime), lastPaused ? 0 : lastRate);

        gameplayClock.OnSeek += onSeek;
    }

    public void Enqueue(UtaPitchFrame frame)
    {
        if (!captureEnabled)
            return;

        UtaCapturedPitchFrame captured = UtaCapturedPitchFrame.FromAnalysis(
            frame.Hertz,
            frame.Clarity,
            frame.Rms,
            frame.ArrivalTimestamp,
            frame.WindowDurationMilliseconds);

        if (debugDiagnosticsEnabled)
            diagnosticEnqueuedFrames++;
        if (!queue.TryEnqueue(captured) && scoringEnabled)
            comparable = false;
    }

    protected override void Update()
    {
        base.Update();

        long now = Stopwatch.GetTimestamp();
        bool paused = gameplayClock.IsPaused.Value;
        double rate = gameplayClock.Rate;

        if (paused != lastPaused)
        {
            mapper.AddAnchor(now, toMicroseconds(gameplayClock.CurrentTime), paused ? 0 : rate);
            lastPaused = paused;
        }
        else if (!paused && Math.Abs(rate - lastRate) > 0.000001)
        {
            mapper.AddAnchor(now, toMicroseconds(gameplayClock.CurrentTime), rate);
        }

        lastRate = rate;

        long latencyMicroseconds = checked((long)Math.Round(
            microphoneLatency.Value * 1000.0,
            MidpointRounding.AwayFromZero));

        if (captureEnabled)
        {
            int drained = queue.DrainTo(
                mapper,
                latencyMicroseconds,
                scoringEnabled ? session : null,
                mappedFrameConsumer,
                maximumFrames: 512);
            if (debugDiagnosticsEnabled)
                diagnosticDrainedFrames += drained;
        }

        if (scoringEnabled)
        {
            UtaMappedGameplayTime watermark = mapper.MapCaptureCentre(now, watermark_safety_margin_microseconds, latencyMicroseconds);
            if (watermark.TimelineEpoch == options.TimelineEpoch)
            {
                long requestedWatermark = Math.Max(0, watermark.SongTimeMicroseconds);

                if (lastScoringWatermarkMicroseconds != long.MinValue && requestedWatermark < lastScoringWatermarkMicroseconds)
                    comparable = false;

                long safeWatermark = Math.Max(lastScoringWatermarkMicroseconds, requestedWatermark);
                lastScoringWatermarkMicroseconds = safeWatermark;

                foreach (UtaNoteScore score in session.AdvanceWatermark(safeWatermark))
                {
                    if (debugDiagnosticsEnabled)
                        diagnosticCompletedNotes++;
                    lastGrade.Value = score.Grade;
                    lastFaults.Value = score.Faults;
                    lastBiasCents.Value = score.BiasCents;
                    completedScores.Add(score);
                    NoteCompleted?.Invoke(score);
                }

                publishCompletedPhrases(safeWatermark);
            }
            else
            {
                if (debugDiagnosticsEnabled)
                    diagnosticEpochMismatches++;
                lastScoringWatermarkMicroseconds = long.MinValue;
            }

            if (queue.Overflowed)
            {
                comparable = false;
                archiveStatus.Value = "Non-comparable: capture queue overflow";
            }
            else
            {
                archiveStatus.Value = Comparable
                    ? string.Empty
                    : "Non-comparable: unstable playback state";
            }
        }
        else
        {
            archiveStatus.Value = string.Empty;
        }

        reportDiagnostics(now);
    }

    private void onMappedFrame(UtaCapturedPitchFrame captured, UtaScoringFrame mapped)
    {
        if (debugDiagnosticsEnabled)
            diagnosticAcceptedFrames++;
        lock (replaySync)
            replayFrames.Add(UtaPerformancePitchFrame.FromMapped(captured, mapped));
        if (mapped.Voiced)
            vocalRangeAdvisor.AddObservation(mapped.PitchCents, mapped.ClarityPermille);
    }

    private void reportDiagnostics(long now)
    {
        if (!debugDiagnosticsEnabled || Stopwatch.GetElapsedTime(diagnosticIntervalStart, now).TotalSeconds < 5)
            return;

        diagnosticIntervalStart = now;
        Logger.Log(
            $"Uta debug scoring: scoringEnabled={scoringEnabled} captureEnabled={captureEnabled} comparable={Comparable} "
            + $"epoch(mapper)={mapper.CurrentTimelineEpoch} epoch(options)={options.TimelineEpoch} epochMismatches={diagnosticEpochMismatches} "
            + $"watermarkUs={lastScoringWatermarkMicroseconds} enqueued={diagnosticEnqueuedFrames} drained={diagnosticDrainedFrames} "
            + $"acceptedByScorer={diagnosticAcceptedFrames} queueOverflowed={queue.Overflowed} completedNotes={diagnosticCompletedNotes} "
            + $"lastGrade={lastGrade.Value} targets={targets.Length} queryAttempts={diagnosticQueryAttempts} "
            + $"querySuccesses={diagnosticQuerySuccesses} nativeApplications={diagnosticNativeApplications} "
            + $"postEndChecks={diagnosticPostEndChecks} commitDelayPassed={diagnosticCommitDelayPassed}");

        diagnosticEnqueuedFrames = 0;
        diagnosticDrainedFrames = 0;
        diagnosticAcceptedFrames = 0;
        diagnosticEpochMismatches = 0;
    }

    public bool TryGetCompletedNote(int scoringIndex, out UtaNoteScore? score)
    {
        if (debugDiagnosticsEnabled)
            diagnosticQueryAttempts++;

        if (!scoringEnabled)
        {
            score = null;
            return false;
        }

        bool found = session.TryGetCompletedNote(scoringIndex, out score);
        if (found && debugDiagnosticsEnabled)
            diagnosticQuerySuccesses++;
        return found;
    }

    public bool TryPreviewCompletedNote(int scoringIndex, out UtaNoteScore? score)
    {
        if (!scoringEnabled)
        {
            score = null;
            return false;
        }

        return session.TryGetCompletedNote(scoringIndex, out score);
    }

    public void RecordNativeApplication()
    {
        if (debugDiagnosticsEnabled)
            diagnosticNativeApplications++;
    }

    public bool TryClaimDiagnosticCheckLogSlot()
        => debugDiagnosticsEnabled && Interlocked.Increment(ref diagnosticCheckLogSlots) <= 8;

    public void RecordPostEndCheck()
    {
        if (debugDiagnosticsEnabled)
            Interlocked.Increment(ref diagnosticPostEndChecks);
    }

    public void RecordCommitDelayPassed()
    {
        if (debugDiagnosticsEnabled)
            Interlocked.Increment(ref diagnosticCommitDelayPassed);
    }

    public bool TryClaimDiagnosticApplyLogSlot()
        => debugDiagnosticsEnabled && Interlocked.Increment(ref diagnosticApplyLogSlots) <= 5;

    public UtaPerformanceScore CompletePerformance()
    {
        if (scoringEnabled)
            return session.CompletePerformance();

        return emptyPerformance ??= new UtaScoringEngine(options).Score(
            Array.Empty<UtaScoringTarget>(),
            Array.Empty<UtaScoringFrame>());
    }

    public IReadOnlyList<UtaPhraseScore> GetPhraseResults()
        => scoringEnabled
            ? UtaPhraseAnalytics.Analyse(CompletePerformance().Notes, beatmap.Transcript)
            : Array.Empty<UtaPhraseScore>();

    private void publishCompletedPhrases(long watermarkMicroseconds)
    {
        while (nextPhraseAnalysisIndex < beatmap.Transcript.Count)
        {
            var segment = beatmap.Transcript[nextPhraseAnalysisIndex];
            long end = checked((long)Math.Round(segment.End * 1_000_000, MidpointRounding.AwayFromZero));
            if (end > watermarkMicroseconds)
                return;

            lastPhraseScore.Value = UtaPhraseAnalytics.AnalysePhrase(
                completedScores,
                segment,
                nextPhraseAnalysisIndex);
            nextPhraseAnalysisIndex++;
        }
    }

    private void resetPhraseTracking(long songTimeMicroseconds)
    {
        completedScores.Clear();
        lastPhraseScore.Value = null;
        nextPhraseAnalysisIndex = 0;

        while (nextPhraseAnalysisIndex < beatmap.Transcript.Count)
        {
            double endSeconds = beatmap.Transcript[nextPhraseAnalysisIndex].End;
            long end = checked((long)Math.Round(endSeconds * 1_000_000, MidpointRounding.AwayFromZero));
            if (end > songTimeMicroseconds)
                break;
            nextPhraseAnalysisIndex++;
        }
    }

    private void onSeek()
    {
        long now = Stopwatch.GetTimestamp();
        int epoch = mapper.AddAnchor(
            now,
            toMicroseconds(gameplayClock.CurrentTime),
            gameplayClock.IsPaused.Value ? 0 : gameplayClock.Rate,
            startsNewTimelineEpoch: true);

        queue.Clear();
        options = new UtaScoringOptions
        {
            TransposeSemitones = (int)MathF.Round(keyShiftSemitones.Value),
            AllowOctaveTolerance = octaveFoldEnabled.Value || beatmap.OctaveTolerance,
            TimelineEpoch = epoch,
        };
        session.Reset(options);
        emptyPerformance = null;
        resetPhraseTracking(toMicroseconds(gameplayClock.CurrentTime));

        if (scoringEnabled)
        {
            comparable = false;
            lastScoringWatermarkMicroseconds = long.MinValue;
            archiveStatus.Value = "Non-comparable: playback seek/loop";
        }
    }

    private void onTransposeChanged(ValueChangedEvent<float> _) => resetRuntimeScoringOptions();

    private void onOctaveFoldChanged(ValueChangedEvent<bool> _) => resetRuntimeScoringOptions();

    private void resetRuntimeScoringOptions()
    {
        long now = Stopwatch.GetTimestamp();
        int epoch = mapper.AddAnchor(
            now,
            toMicroseconds(gameplayClock.CurrentTime),
            gameplayClock.IsPaused.Value ? 0 : gameplayClock.Rate,
            startsNewTimelineEpoch: true);

        queue.Clear();
        options = new UtaScoringOptions
        {
            TransposeSemitones = (int)MathF.Round(keyShiftSemitones.Value),
            AllowOctaveTolerance = octaveFoldEnabled.Value || beatmap.OctaveTolerance,
            TimelineEpoch = epoch,
        };
        session.Reset(options);
        emptyPerformance = null;
        resetPhraseTracking(toMicroseconds(gameplayClock.CurrentTime));
        lastScoringWatermarkMicroseconds = long.MinValue;
        if (scoringEnabled)
        {
            comparable = false;
            archiveStatus.Value = "Non-comparable: scoring option changed during play";
        }
    }

    private static long toMicroseconds(double milliseconds)
        => checked((long)Math.Round(milliseconds * 1000, MidpointRounding.AwayFromZero));

    protected override void Dispose(bool isDisposing)
    {
        if (gameplayClock != null)
            gameplayClock.OnSeek -= onSeek;
        microphoneLatency.UnbindAll();
        keyShiftSemitones.UnbindAll();
        octaveFoldEnabled.UnbindAll();
        debugDiagnostics.UnbindAll();
        base.Dispose(isDisposing);
    }
}
