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
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Mods;
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
    // Doubled and passed as MapCaptureCentre's analysis-window argument (which halves it), giving
    // the watermark ~50ms of slack beyond the configured microphone latency for analysis-window
    // half-width and capture/drain scheduling jitter, on top of what a real captured frame gets.
    private const long watermark_safety_margin_microseconds = 100_000;

    private readonly UtaBeatmap beatmap;
    private readonly UtaScoringTarget[] targets;
    private readonly bool scoringEnabled;
    private readonly bool captureEnabled;
    private readonly UtaCaptureFrameQueue queue = new(4096);
    private readonly UtaGameplayTimelineMapper mapper = new(Stopwatch.Frequency);
    private readonly object replaySync = new();
    private readonly List<UtaPerformancePitchFrame> replayFrames = new();
    private readonly UtaVocalRangeAdvisor vocalRangeAdvisor = new();
    private readonly Bindable<UtaNoteGrade> lastGrade = new();
    private readonly Bindable<UtaPitchFault> lastFaults = new();
    private readonly BindableInt lastBiasCents = new();
    private readonly Bindable<string> archiveStatus = new();

    private GameplayClockContainer gameplayClock = null!;
    private readonly BindableFloat microphoneLatency = new();
    private readonly BindableBool debugDiagnostics = new();
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

    public event Action<UtaNoteScore>? NoteCompleted;

    public bool ScoringEnabled => scoringEnabled;
    public bool CaptureEnabled => captureEnabled;
    public bool Comparable => scoringEnabled && comparable && !queue.Overflowed;
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
    public IBindable<string> ArchiveStatus => archiveStatus;

    public UtaGameplayScoringController(UtaBeatmap beatmap, bool scoringEnabled, bool captureEnabled)
    {
        this.beatmap = beatmap;
        this.scoringEnabled = scoringEnabled;
        this.captureEnabled = captureEnabled;
        targets = UtaScoringBeatmapAdapter.CreateTargets(beatmap).ToArray();
        archiveStatus.Value = string.Empty;
    }

    [BackgroundDependencyLoader]
    private void load(
        GameplayClockContainer gameplayClock,
        UtaAudioSettingsState audioSettings,
        IBindable<IReadOnlyList<Mod>> mods)
    {
        this.gameplayClock = gameplayClock;
        microphoneLatency.BindTo(audioSettings.MicrophoneLatency);
        debugDiagnostics.BindTo(audioSettings.DebugDiagnostics);
        diagnosticIntervalStart = Stopwatch.GetTimestamp();

        options = new UtaScoringOptions
        {
            TransposeSemitones = (int)MathF.Round(audioSettings.KeyShiftSemitones.Value),
            AllowOctaveTolerance = mods.Value.Any(mod => mod is UtaModOctaveFold) || beatmap.OctaveTolerance,
            TimelineEpoch = 0,
        };
        session = new UtaStreamingScoringSession(targets, options);

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
            diagnosticDrainedFrames += queue.DrainTo(
                mapper,
                latencyMicroseconds,
                scoringEnabled ? session : null,
                (captured, mapped) =>
                {
                    diagnosticAcceptedFrames++;
                    lock (replaySync)
                        replayFrames.Add(UtaPerformancePitchFrame.FromMapped(captured, mapped));
                    if (mapped.Voiced)
                        vocalRangeAdvisor.AddObservation(mapped.PitchCents, mapped.ClarityPermille);
                },
                maximumFrames: 512);
        }

        if (scoringEnabled)
        {
            // A frame's mapped song-time is always latency+window behind the real "now" it was
            // captured near (see UtaCapturedPitchFrame.MapToScoringFrame / MapCaptureCentre). Advancing
            // the watermark straight to MapTimestamp(now) - with no such offset - made every frame look
            // "late" the instant it arrived, so TryAddFrame rejected nearly all of them and every note
            // committed as an empty-frame Miss. Mirror the same capture-centre offset here so the
            // watermark trails "now" the way frames do, instead of racing ahead of them.
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
                    diagnosticCompletedNotes++;
                    lastGrade.Value = score.Grade;
                    lastFaults.Value = score.Faults;
                    lastBiasCents.Value = score.BiasCents;
                    NoteCompleted?.Invoke(score);
                }
            }
            else
            {
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

    private void reportDiagnostics(long now)
    {
        if (!debugDiagnostics.Value || Stopwatch.GetElapsedTime(diagnosticIntervalStart, now).TotalSeconds < 5)
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
        diagnosticQueryAttempts++;

        if (!scoringEnabled)
        {
            score = null;
            return false;
        }

        bool found = session.TryGetCompletedNote(scoringIndex, out score);
        if (found)
            diagnosticQuerySuccesses++;
        return found;
    }

    /// <summary>
    /// Same lookup as <see cref="TryGetCompletedNote"/>, for the pitch guide's note-colouring
    /// preview. Kept separate so it does not pollute the native-scoring query diagnostics above.
    /// </summary>
    public bool TryPreviewCompletedNote(int scoringIndex, out UtaNoteScore? score)
    {
        if (!scoringEnabled)
        {
            score = null;
            return false;
        }

        return session.TryGetCompletedNote(scoringIndex, out score);
    }

    /// <summary>Diagnostics-only: records that a drawable successfully applied a native judgement.</summary>
    public void RecordNativeApplication() => diagnosticNativeApplications++;

    /// <summary>Diagnostics-only: caps how many "CheckForResult" log lines get printed across every drawable sharing this controller.</summary>
    public bool TryClaimDiagnosticCheckLogSlot() => Interlocked.Increment(ref diagnosticCheckLogSlots) <= 8;

    /// <summary>Diagnostics-only: a drawable's CheckForResult ran with timeOffset >= 0 (its note has ended).</summary>
    public void RecordPostEndCheck() => Interlocked.Increment(ref diagnosticPostEndChecks);

    /// <summary>Diagnostics-only: a drawable's CheckForResult passed the commit-delay gate.</summary>
    public void RecordCommitDelayPassed() => Interlocked.Increment(ref diagnosticCommitDelayPassed);

    /// <summary>Diagnostics-only: caps how many "apply" log lines get printed across every drawable sharing this controller.</summary>
    public bool TryClaimDiagnosticApplyLogSlot() => Interlocked.Increment(ref diagnosticApplyLogSlots) <= 5;

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

    private void onSeek()
    {
        long now = Stopwatch.GetTimestamp();
        int epoch = mapper.AddAnchor(
            now,
            toMicroseconds(gameplayClock.CurrentTime),
            gameplayClock.IsPaused.Value ? 0 : gameplayClock.Rate,
            startsNewTimelineEpoch: true);

        // A seek/loop creates a fresh deterministic epoch. lazer owns native
        // judgement reversion; this session never mixes frames across epochs.
        queue.Clear();
        options = new UtaScoringOptions
        {
            TransposeSemitones = options.TransposeSemitones,
            AllowOctaveTolerance = options.AllowOctaveTolerance,
            TimelineEpoch = epoch,
        };
        session = new UtaStreamingScoringSession(targets, options);
        emptyPerformance = null;

        if (scoringEnabled)
        {
            comparable = false;
            lastScoringWatermarkMicroseconds = long.MinValue;
            archiveStatus.Value = "Non-comparable: playback seek/loop";
        }
    }

    private static long toMicroseconds(double milliseconds)
        => checked((long)Math.Round(milliseconds * 1000, MidpointRounding.AwayFromZero));

    protected override void Dispose(bool isDisposing)
    {
        if (gameplayClock != null)
            gameplayClock.OnSeek -= onSeek;
        microphoneLatency.UnbindAll();
        debugDiagnostics.UnbindAll();
        base.Dispose(isDisposing);
    }
}
