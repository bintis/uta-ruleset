// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// Non-blocking boundary between the microphone analysis worker and gameplay.
/// Raw capture frames are mapped to song time only while draining on the
/// gameplay thread. Overflow is observable and must invalidate a comparable
/// performance rather than silently changing its score.
/// </summary>
public sealed class UtaCaptureFrameQueue
{
    private readonly object sync = new();
    private readonly Queue<UtaCapturedPitchFrame> frames;

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (sync)
                return frames.Count;
        }
    }

    private long rejectedFrames;

    public long RejectedFrames => Interlocked.Read(ref rejectedFrames);

    public bool Overflowed => RejectedFrames > 0;

    public UtaCaptureFrameQueue(int capacity = 4096)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        Capacity = capacity;
        frames = new Queue<UtaCapturedPitchFrame>(capacity);
    }

    public bool TryEnqueue(UtaCapturedPitchFrame frame)
    {
        frame.Validate();

        lock (sync)
        {
            if (frames.Count >= Capacity)
            {
                Interlocked.Increment(ref rejectedFrames);
                return false;
            }

            frames.Enqueue(frame);
            return true;
        }
    }

    /// <summary>
    /// Maps and drains capture frames. When a formal scoring session is supplied,
    /// only frames accepted by that session reach the optional consumer. A null
    /// session maps frames for recording replay without retaining a second copy
    /// in the scoring engine.
    /// </summary>
    public int DrainTo(
        UtaGameplayTimelineMapper mapper,
        long microphoneLatencyMicroseconds,
        UtaStreamingScoringSession? session,
        Action<UtaCapturedPitchFrame, UtaScoringFrame>? mappedFrameConsumer = null,
        int maximumFrames = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        if (maximumFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFrames));

        int drained = 0;
        while (drained < maximumFrames)
        {
            UtaCapturedPitchFrame captured;
            lock (sync)
            {
                if (frames.Count == 0)
                    break;
                captured = frames.Dequeue();
            }

            UtaScoringFrame mapped = captured.MapToScoringFrame(mapper, microphoneLatencyMicroseconds);
            if (mapped.TimeMicroseconds >= 0
                && (session == null || session.TryAddFrame(mapped)))
                mappedFrameConsumer?.Invoke(captured, mapped);
            drained++;
        }

        return drained;
    }

    public void Clear(bool resetOverflow = false)
    {
        lock (sync)
        {
            frames.Clear();
            if (resetOverflow)
                Interlocked.Exchange(ref rejectedFrames, 0);
        }
    }
}
