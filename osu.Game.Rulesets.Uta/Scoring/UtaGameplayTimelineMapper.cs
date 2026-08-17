// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// Maps monotonic microphone-capture timestamps into song time. Anchors preserve
/// historical rate and seek state, so a frame captured before a seek continues
/// to map through the old timeline segment even if analysis finishes later.
/// </summary>
public sealed class UtaGameplayTimelineMapper
{
    public const int RATE_SCALE = 1_000_000;

    private readonly long timestampFrequency;
    private readonly List<TimelineSegment> segments = new();

    public int CurrentTimelineEpoch => segments.Count == 0 ? 0 : segments[^1].TimelineEpoch;

    public UtaGameplayTimelineMapper(long timestampFrequency)
    {
        if (timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));

        this.timestampFrequency = timestampFrequency;
    }

    public void Reset(long monotonicTimestamp, long songTimeMicroseconds, double playbackRate = 1, int timelineEpoch = 0)
    {
        if (monotonicTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(monotonicTimestamp));
        if (timelineEpoch < 0)
            throw new ArgumentOutOfRangeException(nameof(timelineEpoch));

        segments.Clear();
        segments.Add(new TimelineSegment(monotonicTimestamp, songTimeMicroseconds, quantiseRate(playbackRate), timelineEpoch));
    }

    /// <summary>
    /// Adds a new clock anchor. Set <paramref name="startsNewTimelineEpoch"/> for
    /// backward seeks, A-B loop repeats and practice retries. Ordinary pause,
    /// resume and playback-rate changes remain in the same epoch.
    /// </summary>
    public int AddAnchor(
        long monotonicTimestamp,
        long songTimeMicroseconds,
        double playbackRate,
        bool startsNewTimelineEpoch = false)
    {
        if (segments.Count == 0)
        {
            Reset(monotonicTimestamp, songTimeMicroseconds, playbackRate);
            return CurrentTimelineEpoch;
        }
        if (monotonicTimestamp < segments[^1].MonotonicTimestamp)
            throw new ArgumentOutOfRangeException(nameof(monotonicTimestamp), "Timeline anchors must be monotonic.");

        int epoch = checked(CurrentTimelineEpoch + (startsNewTimelineEpoch ? 1 : 0));
        var segment = new TimelineSegment(monotonicTimestamp, songTimeMicroseconds, quantiseRate(playbackRate), epoch);
        if (monotonicTimestamp == segments[^1].MonotonicTimestamp)
            segments[^1] = segment;
        else
            segments.Add(segment);

        return epoch;
    }

    public UtaMappedGameplayTime MapTimestamp(long monotonicTimestamp)
    {
        if (segments.Count == 0)
            throw new InvalidOperationException("The timeline mapper has not been initialised.");
        if (monotonicTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(monotonicTimestamp));

        TimelineSegment segment = segmentAt(monotonicTimestamp);
        long elapsedTicks = monotonicTimestamp - segment.MonotonicTimestamp;
        Int128 songDeltaNumerator = (Int128)elapsedTicks * 1_000_000 * segment.RateMillionths;
        long songDelta = checked((long)roundDivide(songDeltaNumerator, (Int128)timestampFrequency * RATE_SCALE));
        return new UtaMappedGameplayTime(checked(segment.SongTimeMicroseconds + songDelta), segment.TimelineEpoch);
    }

    public UtaMappedGameplayTime MapCaptureCentre(
        long arrivalTimestamp,
        long analysisWindowDurationMicroseconds,
        long microphoneLatencyMicroseconds)
    {
        if (arrivalTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(arrivalTimestamp));
        if (analysisWindowDurationMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(analysisWindowDurationMicroseconds));

        // Microphone latency is intentionally signed. A negative calibration compares
        // the captured voice with a later song position, matching the public Uta
        // configuration range (-500 ms to +1000 ms).
        long realTimeOffset = checked(analysisWindowDurationMicroseconds / 2 + microphoneLatencyMicroseconds);
        long offsetTicks = checked((long)roundDivide((Int128)realTimeOffset * timestampFrequency, 1_000_000));
        long captureTimestamp = checked(arrivalTimestamp - offsetTicks);
        if (captureTimestamp < 0)
            captureTimestamp = 0;
        return MapTimestamp(captureTimestamp);
    }

    private TimelineSegment segmentAt(long timestamp)
    {
        int low = 0;
        int high = segments.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (segments[middle].MonotonicTimestamp <= timestamp)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return segments[Math.Max(0, high)];
    }

    private static int quantiseRate(double playbackRate)
    {
        if (!double.IsFinite(playbackRate) || playbackRate is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(playbackRate));

        return checked((int)Math.Round(playbackRate * RATE_SCALE, MidpointRounding.AwayFromZero));
    }

    private static Int128 roundDivide(Int128 numerator, Int128 denominator)
    {
        if (denominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));
        if (numerator >= 0)
            return (numerator + denominator / 2) / denominator;
        return -((-numerator + denominator / 2) / denominator);
    }

    private readonly record struct TimelineSegment(
        long MonotonicTimestamp,
        long SongTimeMicroseconds,
        int RateMillionths,
        int TimelineEpoch);
}

public readonly record struct UtaMappedGameplayTime(long SongTimeMicroseconds, int TimelineEpoch);
