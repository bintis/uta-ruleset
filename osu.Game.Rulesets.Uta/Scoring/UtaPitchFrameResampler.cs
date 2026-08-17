// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.Uta.Scoring;

internal readonly record struct UtaResampledPitch(int PitchCents, ushort ClarityPermille, bool Voiced);

internal sealed class UtaPitchFrameResampler
{
    private readonly UtaScoringFrame[] frames;
    private readonly UtaScoringOptions options;

    public UtaPitchFrameResampler(IEnumerable<UtaScoringFrame> source, UtaScoringOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.options = options;

        int capacity = source switch
        {
            ICollection<UtaScoringFrame> collection => collection.Count,
            IReadOnlyCollection<UtaScoringFrame> collection => collection.Count,
            _ => 0,
        };
        var selected = capacity > 0 ? new List<UtaScoringFrame>(capacity) : new List<UtaScoringFrame>();

        foreach (UtaScoringFrame frame in source)
        {
            validateFrame(frame);
            if (frame.TimelineEpoch == options.TimelineEpoch && frame.TimeMicroseconds >= 0)
                selected.Add(frame);
        }

        frames = normalise(selected);
    }

    public UtaPitchFrameResampler(ReadOnlySpan<UtaScoringFrame> source, UtaScoringOptions options)
    {
        this.options = options;
        var selected = new List<UtaScoringFrame>(source.Length);

        foreach (UtaScoringFrame frame in source)
        {
            validateFrame(frame);
            if (frame.TimelineEpoch == options.TimelineEpoch && frame.TimeMicroseconds >= 0)
                selected.Add(frame);
        }

        frames = normalise(selected);
    }

    public UtaResampledPitch SampleAt(long timeMicroseconds)
    {
        if (frames.Length == 0)
            return default;

        int index = Array.BinarySearch(frames, new UtaScoringFrame(timeMicroseconds, 0, 0, false), FrameTimeComparer.Instance);
        if (index >= 0)
            return convert(frames[index]);

        int insertion = ~index;
        UtaScoringFrame? left = insertion > 0 ? frames[insertion - 1] : null;
        UtaScoringFrame? right = insertion < frames.Length ? frames[insertion] : null;

        if (left is { } leftFrame && right is { } rightFrame)
        {
            long gap = rightFrame.TimeMicroseconds - leftFrame.TimeMicroseconds;
            if (gap > 0
                && gap <= options.MaximumInterpolationGapMicroseconds
                && isUsable(leftFrame)
                && isUsable(rightFrame))
            {
                long offset = timeMicroseconds - leftFrame.TimeMicroseconds;
                int pitch = checked(leftFrame.PitchCents + (int)UtaScoringMath.RoundDivide((rightFrame.PitchCents - leftFrame.PitchCents) * offset, gap));
                ushort clarity = checked((ushort)(leftFrame.ClarityPermille
                    + UtaScoringMath.RoundDivide((rightFrame.ClarityPermille - leftFrame.ClarityPermille) * offset, gap)));
                return new UtaResampledPitch(pitch, clarity, true);
            }
        }

        UtaScoringFrame? nearest = nearestTo(timeMicroseconds, left, right);
        if (nearest is not { } nearestFrame
            || Math.Abs(nearestFrame.TimeMicroseconds - timeMicroseconds) > options.MaximumNearestFrameDistanceMicroseconds)
            return default;

        return convert(nearestFrame);
    }

    private UtaScoringFrame[] normalise(List<UtaScoringFrame> selected)
    {
        if (selected.Count == 0)
            return Array.Empty<UtaScoringFrame>();

        // The previous LINQ pipeline grouped by timestamp and selected the best frame using
        // usable -> clarity -> voiced -> pitch ordering, then sorted by time. One sort with
        // the same keys plus an in-place unique pass produces the identical sequence without
        // GroupBy/OrderBy iterator and grouping allocations.
        selected.Sort(compareNormalisedFrames);

        int uniqueCount = 1;
        for (int read = 1; read < selected.Count; read++)
        {
            if (selected[read].TimeMicroseconds == selected[uniqueCount - 1].TimeMicroseconds)
                continue;

            selected[uniqueCount++] = selected[read];
        }

        var result = new UtaScoringFrame[uniqueCount];
        selected.CopyTo(0, result, 0, uniqueCount);
        return result;
    }

    private int compareNormalisedFrames(UtaScoringFrame x, UtaScoringFrame y)
    {
        int comparison = x.TimeMicroseconds.CompareTo(y.TimeMicroseconds);
        if (comparison != 0)
            return comparison;

        comparison = isUsable(y).CompareTo(isUsable(x));
        if (comparison != 0)
            return comparison;

        comparison = y.ClarityPermille.CompareTo(x.ClarityPermille);
        if (comparison != 0)
            return comparison;

        comparison = y.Voiced.CompareTo(x.Voiced);
        if (comparison != 0)
            return comparison;

        return x.PitchCents.CompareTo(y.PitchCents);
    }

    private UtaScoringFrame? nearestTo(long timeMicroseconds, UtaScoringFrame? left, UtaScoringFrame? right)
    {
        if (left == null)
            return right;
        if (right == null)
            return left;

        long leftDistance = timeMicroseconds - left.Value.TimeMicroseconds;
        long rightDistance = right.Value.TimeMicroseconds - timeMicroseconds;
        if (leftDistance < rightDistance)
            return left;
        if (rightDistance < leftDistance)
            return right;

        bool leftUsable = isUsable(left.Value);
        bool rightUsable = isUsable(right.Value);
        if (leftUsable != rightUsable)
            return leftUsable ? right : left; // Conservative at a voiced/unvoiced boundary.
        if (left.Value.ClarityPermille != right.Value.ClarityPermille)
            return left.Value.ClarityPermille > right.Value.ClarityPermille ? left : right;
        return left;
    }

    private UtaResampledPitch convert(UtaScoringFrame frame)
        => isUsable(frame)
            ? new UtaResampledPitch(frame.PitchCents, frame.ClarityPermille, true)
            : new UtaResampledPitch(0, frame.ClarityPermille, false);

    private bool isUsable(UtaScoringFrame frame)
        => frame.Voiced && frame.ClarityPermille >= options.MinimumClarityPermille;

    private static void validateFrame(UtaScoringFrame frame)
    {
        if (frame.ClarityPermille > UtaScoringOptions.QUALITY_SCALE)
            throw new ArgumentException("A scoring frame has clarity outside 0-1000.", "source");
        if (frame.Voiced && frame.PitchCents is < 0 or > 12_700)
            throw new ArgumentException("A voiced scoring frame has pitch outside MIDI 0-127.", "source");
        if (frame.TimelineEpoch < 0)
            throw new ArgumentException("A scoring frame has a negative timeline epoch.", "source");
    }

    private sealed class FrameTimeComparer : IComparer<UtaScoringFrame>
    {
        public static readonly FrameTimeComparer Instance = new();

        public int Compare(UtaScoringFrame x, UtaScoringFrame y) => x.TimeMicroseconds.CompareTo(y.TimeMicroseconds);
    }
}

/// <summary>
/// Allocation-free resampler for the streaming session's already validated, time-ordered
/// active window. Duplicate timestamps are resolved with the exact same preference order as
/// <see cref="UtaPitchFrameResampler"/>.
/// </summary>
internal readonly ref struct UtaRealtimePitchFrameResampler
{
    private readonly ReadOnlySpan<UtaScoringFrame> frames;
    private readonly UtaScoringOptions options;

    public UtaRealtimePitchFrameResampler(ReadOnlySpan<UtaScoringFrame> frames, UtaScoringOptions options)
    {
        this.frames = frames;
        this.options = options;

        long previousTime = long.MinValue;
        foreach (UtaScoringFrame frame in frames)
        {
            if (frame.ClarityPermille > UtaScoringOptions.QUALITY_SCALE)
                throw new ArgumentException("A scoring frame has clarity outside 0-1000.", nameof(frames));
            if (frame.Voiced && frame.PitchCents is < 0 or > 12_700)
                throw new ArgumentException("A voiced scoring frame has pitch outside MIDI 0-127.", nameof(frames));
            if (frame.TimelineEpoch < 0)
                throw new ArgumentException("A scoring frame has a negative timeline epoch.", nameof(frames));
            if (frame.TimelineEpoch != options.TimelineEpoch || frame.TimeMicroseconds < 0)
                throw new ArgumentException("Realtime scoring frames must belong to the active non-negative timeline.", nameof(frames));
            if (frame.TimeMicroseconds < previousTime)
                throw new ArgumentException("Realtime scoring frames must be ordered by time.", nameof(frames));

            previousTime = frame.TimeMicroseconds;
        }
    }

    public UtaResampledPitch SampleAt(long timeMicroseconds)
    {
        if (frames.Length == 0)
            return default;

        int insertion = lowerBound(timeMicroseconds);
        if (insertion < frames.Length && frames[insertion].TimeMicroseconds == timeMicroseconds)
            return convert(bestInGroup(insertion));

        UtaScoringFrame? left = insertion > 0 ? bestInGroup(findGroupStart(insertion - 1)) : null;
        UtaScoringFrame? right = insertion < frames.Length ? bestInGroup(insertion) : null;

        if (left is { } leftFrame && right is { } rightFrame)
        {
            long gap = rightFrame.TimeMicroseconds - leftFrame.TimeMicroseconds;
            if (gap > 0
                && gap <= options.MaximumInterpolationGapMicroseconds
                && isUsable(leftFrame)
                && isUsable(rightFrame))
            {
                long offset = timeMicroseconds - leftFrame.TimeMicroseconds;
                int pitch = checked(leftFrame.PitchCents + (int)UtaScoringMath.RoundDivide((rightFrame.PitchCents - leftFrame.PitchCents) * offset, gap));
                ushort clarity = checked((ushort)(leftFrame.ClarityPermille
                    + UtaScoringMath.RoundDivide((rightFrame.ClarityPermille - leftFrame.ClarityPermille) * offset, gap)));
                return new UtaResampledPitch(pitch, clarity, true);
            }
        }

        UtaScoringFrame? nearest = nearestTo(timeMicroseconds, left, right);
        if (nearest is not { } nearestFrame
            || Math.Abs(nearestFrame.TimeMicroseconds - timeMicroseconds) > options.MaximumNearestFrameDistanceMicroseconds)
            return default;

        return convert(nearestFrame);
    }

    private int lowerBound(long timeMicroseconds)
    {
        int low = 0;
        int high = frames.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (frames[middle].TimeMicroseconds < timeMicroseconds)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private int findGroupStart(int index)
    {
        long time = frames[index].TimeMicroseconds;
        while (index > 0 && frames[index - 1].TimeMicroseconds == time)
            index--;
        return index;
    }

    private UtaScoringFrame bestInGroup(int start)
    {
        UtaScoringFrame best = frames[start];
        long time = best.TimeMicroseconds;
        for (int i = start + 1; i < frames.Length && frames[i].TimeMicroseconds == time; i++)
        {
            if (isBetter(frames[i], best))
                best = frames[i];
        }

        return best;
    }

    private bool isBetter(UtaScoringFrame candidate, UtaScoringFrame current)
    {
        bool candidateUsable = isUsable(candidate);
        bool currentUsable = isUsable(current);
        if (candidateUsable != currentUsable)
            return candidateUsable;
        if (candidate.ClarityPermille != current.ClarityPermille)
            return candidate.ClarityPermille > current.ClarityPermille;
        if (candidate.Voiced != current.Voiced)
            return candidate.Voiced;
        return candidate.PitchCents < current.PitchCents;
    }

    private UtaScoringFrame? nearestTo(long timeMicroseconds, UtaScoringFrame? left, UtaScoringFrame? right)
    {
        if (left == null)
            return right;
        if (right == null)
            return left;

        long leftDistance = timeMicroseconds - left.Value.TimeMicroseconds;
        long rightDistance = right.Value.TimeMicroseconds - timeMicroseconds;
        if (leftDistance < rightDistance)
            return left;
        if (rightDistance < leftDistance)
            return right;

        bool leftUsable = isUsable(left.Value);
        bool rightUsable = isUsable(right.Value);
        if (leftUsable != rightUsable)
            return leftUsable ? right : left;
        if (left.Value.ClarityPermille != right.Value.ClarityPermille)
            return left.Value.ClarityPermille > right.Value.ClarityPermille ? left : right;
        return left;
    }

    private UtaResampledPitch convert(UtaScoringFrame frame)
        => isUsable(frame)
            ? new UtaResampledPitch(frame.PitchCents, frame.ClarityPermille, true)
            : new UtaResampledPitch(0, frame.ClarityPermille, false);

    private bool isUsable(UtaScoringFrame frame)
        => frame.Voiced && frame.ClarityPermille >= options.MinimumClarityPermille;
}
