// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// A deterministic streaming wrapper around <see cref="UtaScoringEngine"/>.
/// The caller supplies mapped song-time frames and advances a capture-time
/// watermark. Notes are committed only after the configured delay, allowing
/// late pitch-analysis windows to arrive without depending on render timing.
/// </summary>
public sealed class UtaStreamingScoringSession
{
    private readonly object sync = new();
    private readonly UtaScoringTarget[] targets;
    private UtaScoringOptions options;
    private UtaScoringEngine engine;
    private long realtimeContextMicroseconds;
    private readonly long?[] earliestRequiredFrameTimes;

    // The full list is retained for the one final performance calculation and
    // archive. Realtime note commits must never repeatedly rescore this list:
    // doing so makes every later note more expensive than the previous one.
    private readonly List<UtaScoringFrame> allFrames;
    private readonly List<UtaScoringFrame> realtimeFrames;
    private readonly Dictionary<int, UtaNoteScore> completed;

    private UtaPerformanceScore? completedPerformance;
    private int realtimeStartIndex;
    private int nextTarget;
    private long lastWatermark = long.MinValue;
    private long rejectedLateFrames;

    public IReadOnlyDictionary<int, UtaNoteScore> CompletedNotes
    {
        get
        {
            lock (sync)
                return new Dictionary<int, UtaNoteScore>(completed);
        }
    }

    public long RejectedLateFrames
    {
        get
        {
            lock (sync)
                return rejectedLateFrames;
        }
    }

    /// <summary>
    /// Largest frame window passed to a realtime single-note score operation.
    /// Exposed to the test assembly as a regression guard against accidentally
    /// reintroducing whole-performance rescoring.
    /// </summary>
    internal int MaximumRealtimeFrameWindow { get; private set; }

    internal int RealtimeBufferedFrameCount
    {
        get
        {
            lock (sync)
                return realtimeFrames.Count - realtimeStartIndex;
        }
    }

    public UtaStreamingScoringSession(IEnumerable<UtaScoringTarget> targets, UtaScoringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        this.options = options ?? new UtaScoringOptions();
        this.options.Validate();
        UtaScoringTarget[] targetArray = targets.ToArray();
        engine = new UtaScoringEngine(this.options);
        engine.ValidateTargets(targetArray.OrderBy(target => target.StartTimeMicroseconds)
                                          .ThenBy(target => target.Index)
                                          .ToArray());
        this.targets = targetArray.OrderBy(target => target.EndTimeMicroseconds)
                                  .ThenBy(target => target.Index)
                                  .ToArray();

        long performanceEnd = this.targets.Length == 0 ? 0 : this.targets[^1].EndTimeMicroseconds;
        int expectedFrameCapacity = (int)Math.Clamp(performanceEnd / 10_000 + 64, 256, 32_768);
        allFrames = new List<UtaScoringFrame>(expectedFrameCapacity);
        realtimeFrames = new List<UtaScoringFrame>(Math.Min(expectedFrameCapacity, 512));
        completed = new Dictionary<int, UtaNoteScore>(this.targets.Length);

        realtimeContextMicroseconds = Math.Max(
            this.options.MaximumInterpolationGapMicroseconds,
            this.options.MaximumNearestFrameDistanceMicroseconds);
        earliestRequiredFrameTimes = new long?[this.targets.Length + 1];
        rebuildEarliestRequiredFrameTimes();
    }

    /// <summary>
    /// Starts a fresh deterministic timeline while retaining already allocated collections.
    /// Used by practice seeks/loops to avoid rebuilding large backing arrays on every repeat.
    /// </summary>
    internal void Reset(UtaScoringOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        lock (sync)
        {
            this.options = options;
            // Target structure was validated in the constructor and does not change across
            // gameplay seeks. The controller only advances TimelineEpoch here, so repeating
            // HashSet/index validation would create avoidable practice-loop garbage.
            engine = new UtaScoringEngine(options);
            realtimeContextMicroseconds = Math.Max(
                options.MaximumInterpolationGapMicroseconds,
                options.MaximumNearestFrameDistanceMicroseconds);

            allFrames.Clear();
            realtimeFrames.Clear();
            completed.Clear();
            completedPerformance = null;
            realtimeStartIndex = 0;
            nextTarget = 0;
            lastWatermark = long.MinValue;
            rejectedLateFrames = 0;
            MaximumRealtimeFrameWindow = 0;
            rebuildEarliestRequiredFrameTimes();
        }
    }

    public void AddFrame(UtaScoringFrame frame) => TryAddFrame(frame);

    /// <summary>
    /// Adds a mapped frame if it belongs to the active epoch and has not arrived
    /// behind the committed capture watermark. Late frames are rejected rather
    /// than changing a note after its native judgement has been emitted.
    /// </summary>
    public bool TryAddFrame(UtaScoringFrame frame)
    {
        if (frame.TimelineEpoch != options.TimelineEpoch)
            return false;
        if (frame.TimeMicroseconds < 0)
            return false; // Capture during gameplay lead-in is outside every scoring target.
        if (frame.ClarityPermille > UtaScoringOptions.QUALITY_SCALE)
            throw new ArgumentOutOfRangeException(nameof(frame));
        if (frame.Voiced && frame.PitchCents is < 0 or > 12_700)
            throw new ArgumentOutOfRangeException(nameof(frame));

        lock (sync)
        {
            if (lastWatermark != long.MinValue && frame.TimeMicroseconds <= lastWatermark)
            {
                rejectedLateFrames++;
                return false;
            }

            allFrames.Add(frame);
            completedPerformance = null;

            long? earliestRequired = getEarliestRequiredFrameTime();
            if (earliestRequired != null && frame.TimeMicroseconds >= earliestRequired.Value)
                addRealtimeFrame(frame);

            return true;
        }
    }

    public IReadOnlyList<UtaNoteScore> AdvanceWatermark(long songTimeMicroseconds)
    {
        lock (sync)
        {
            if (songTimeMicroseconds < lastWatermark)
            {
                // Defensive recovery for rare timeline regressions (e.g. abrupt
                // clock source corrections). A non-decreasing watermark is a
                // hard requirement for replay validity, but this path should not
                // crash the ruleset; we simply ignore the out-of-order request.
                return Array.Empty<UtaNoteScore>();
            }
            lastWatermark = songTimeMicroseconds;

            // This method runs once per gameplay update. Most updates complete no note,
            // so do not allocate the result List until the first target actually becomes due.
            if (nextTarget >= targets.Length
                || addSaturating(targets[nextTarget].EndTimeMicroseconds, options.CommitDelayMicroseconds) > songTimeMicroseconds)
                return Array.Empty<UtaNoteScore>();

            List<UtaNoteScore>? newlyCompleted = null;
            while (nextTarget < targets.Length
                   && addSaturating(targets[nextTarget].EndTimeMicroseconds, options.CommitDelayMicroseconds) <= songTimeMicroseconds)
            {
                UtaScoringTarget target = targets[nextTarget++];
                UtaNoteScore score = scoreRealtimeTarget(target);
                completed[target.Index] = score;
                (newlyCompleted ??= new List<UtaNoteScore>(1)).Add(score);

                trimRealtimeFrames();
            }

            return newlyCompleted == null ? Array.Empty<UtaNoteScore>() : newlyCompleted;
        }
    }

    public bool TryGetCompletedNote(int scoringIndex, out UtaNoteScore? score)
    {
        lock (sync)
            return completed.TryGetValue(scoringIndex, out score);
    }

    /// <summary>
    /// Calculates the full report at most once for a stable frame set. Recording
    /// finalisation requests both the total and phrase analysis, so caching here
    /// avoids a second full sort/resample pass at the end of the song.
    /// </summary>
    public UtaPerformanceScore CompletePerformance()
    {
        lock (sync)
            return completedPerformance ??= engine.Score(targets, allFrames);
    }

    private UtaNoteScore scoreRealtimeTarget(UtaScoringTarget target)
    {
        if (!UtaScoringMath.IsScorable(target, options))
        {
            MaximumRealtimeFrameWindow = Math.Max(MaximumRealtimeFrameWindow, 0);
            return engine.ScoreNote(target, ReadOnlySpan<UtaScoringFrame>.Empty);
        }

        long start = subtractSaturating(target.StartTimeMicroseconds, realtimeContextMicroseconds);
        long end = addSaturating(target.EndTimeMicroseconds, realtimeContextMicroseconds);

        int first = lowerBound(start, realtimeStartIndex);
        int afterLast = upperBound(end, first);
        int count = afterLast - first;
        MaximumRealtimeFrameWindow = Math.Max(MaximumRealtimeFrameWindow, count);

        if (count <= 0)
            return engine.ScoreNote(target, ReadOnlySpan<UtaScoringFrame>.Empty);

        // realtimeFrames is not modified while sync is held. Exposing its active slice as a
        // span therefore removes the per-note CopyTo/new-array allocation from the hot path.
        ReadOnlySpan<UtaScoringFrame> window = CollectionsMarshal.AsSpan(realtimeFrames).Slice(first, count);
        return engine.ScoreNote(target, window);
    }

    private void trimRealtimeFrames()
    {
        long? earliestRequired = getEarliestRequiredFrameTime();
        if (earliestRequired == null)
        {
            realtimeFrames.Clear();
            realtimeStartIndex = 0;
            return;
        }

        realtimeStartIndex = lowerBound(earliestRequired.Value, realtimeStartIndex);

        // Keep prefix removal amortised. In normal gameplay the active slice is
        // tiny; this also stays linear when a test or replay preloads the whole
        // song before advancing its watermark.
        if (realtimeStartIndex >= 4096 && realtimeStartIndex * 2 >= realtimeFrames.Count)
        {
            realtimeFrames.RemoveRange(0, realtimeStartIndex);
            realtimeStartIndex = 0;
        }
    }

    private void addRealtimeFrame(UtaScoringFrame frame)
    {
        if (realtimeFrames.Count == realtimeStartIndex
            || frame.TimeMicroseconds >= realtimeFrames[^1].TimeMicroseconds)
        {
            realtimeFrames.Add(frame);
            return;
        }

        int insertion = lowerBound(frame.TimeMicroseconds, realtimeStartIndex);
        realtimeFrames.Insert(insertion, frame);
    }

    private int lowerBound(long timeMicroseconds, int start)
    {
        int low = start;
        int high = realtimeFrames.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (realtimeFrames[middle].TimeMicroseconds < timeMicroseconds)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private int upperBound(long timeMicroseconds, int start)
    {
        int low = start;
        int high = realtimeFrames.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (realtimeFrames[middle].TimeMicroseconds <= timeMicroseconds)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private void rebuildEarliestRequiredFrameTimes()
    {
        long? earliestRequired = null;
        earliestRequiredFrameTimes[targets.Length] = null;
        for (int i = targets.Length - 1; i >= 0; i--)
        {
            UtaScoringTarget target = targets[i];
            if (UtaScoringMath.IsScorable(target, options))
                earliestRequired = subtractSaturating(target.StartTimeMicroseconds, realtimeContextMicroseconds);

            earliestRequiredFrameTimes[i] = earliestRequired;
        }
    }

    private long? getEarliestRequiredFrameTime() => earliestRequiredFrameTimes[nextTarget];

    private static long subtractSaturating(long value, long amount)
        => value < long.MinValue + amount ? long.MinValue : value - amount;

    private static long addSaturating(long value, long amount)
        => value > long.MaxValue - amount ? long.MaxValue : value + amount;
}
