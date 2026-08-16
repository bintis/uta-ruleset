// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

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

        UtaScoringFrame[] all = source.ToArray();
        validateFrames(all);
        UtaScoringFrame[] selected = all.Where(frame => frame.TimelineEpoch == options.TimelineEpoch && frame.TimeMicroseconds >= 0)
                                        .ToArray();

        frames = selected.GroupBy(frame => frame.TimeMicroseconds)
                         .Select(group => group.OrderByDescending(isUsable)
                                               .ThenByDescending(frame => frame.ClarityPermille)
                                               .ThenByDescending(frame => frame.Voiced)
                                               .ThenBy(frame => frame.PitchCents)
                                               .First())
                         .OrderBy(frame => frame.TimeMicroseconds)
                         .ToArray();
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

    private static void validateFrames(IEnumerable<UtaScoringFrame> source)
    {
        foreach (UtaScoringFrame frame in source)
        {
            if (frame.ClarityPermille > UtaScoringOptions.QUALITY_SCALE)
                throw new ArgumentException("A scoring frame has clarity outside 0-1000.", nameof(source));
            if (frame.Voiced && frame.PitchCents is < 0 or > 12_700)
                throw new ArgumentException("A voiced scoring frame has pitch outside MIDI 0-127.", nameof(source));
            if (frame.TimelineEpoch < 0)
                throw new ArgumentException("A scoring frame has a negative timeline epoch.", nameof(source));
        }
    }

    private sealed class FrameTimeComparer : IComparer<UtaScoringFrame>
    {
        public static readonly FrameTimeComparer Instance = new();

        public int Compare(UtaScoringFrame x, UtaScoringFrame y) => x.TimeMicroseconds.CompareTo(y.TimeMicroseconds);
    }
}
