// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Karaoke.Integration.Uta;

namespace osu.Game.Rulesets.Karaoke.UI.Uta;

public static class UtaLyricsTimeline
{
    public const double LYRICS_LEAD = 0.15;
    public const double WORD_HIGHLIGHT_LEAD = 0.25;
    public const double SEGMENT_LINGER = 0.5;
    public const double COUNTDOWN_DURATION = 3;
    public const double COUNTDOWN_GAP_THRESHOLD = 3.5;

    public static IReadOnlyList<UtaTranscriptSegment> Normalize(IEnumerable<UtaTranscriptSegment> source)
    {
        return source.Select(segment => segment.Words.Count > 0 ? segment : withEstimatedWords(segment)).ToArray();
    }

    public static int FindCurrentSegment(IReadOnlyList<UtaTranscriptSegment> segments, double time, int hint = 0)
    {
        if (segments.Count == 0)
            return -1;

        int start = hint < segments.Count && time >= segments[hint].Start - LYRICS_LEAD ? hint : 0;

        for (int i = start; i < segments.Count; i++)
        {
            if (time >= segments[i].End + SEGMENT_LINGER)
            {
                int next = i + 1;
                if (next < segments.Count
                    && segments[next].Start - segments[i].End < COUNTDOWN_GAP_THRESHOLD
                    && time < segments[next].Start - LYRICS_LEAD)
                {
                    return i;
                }

                continue;
            }

            int following = i + 1;
            if (following < segments.Count && time >= segments[following].Start - LYRICS_LEAD)
                return following;

            return i;
        }

        return segments.Count - 1;
    }

    public static UtaLyricsFrame Evaluate(IReadOnlyList<UtaTranscriptSegment> segments, double time, int hint = 0)
    {
        int index = FindCurrentSegment(segments, time, hint);
        if (index < 0)
            return new UtaLyricsFrame(-1, false, null, Array.Empty<double>());

        var segment = segments[index];
        bool active = time >= segment.Start - LYRICS_LEAD && time <= segment.End + SEGMENT_LINGER;
        double gapBefore = index == 0 ? segment.Start : segment.Start - segments[index - 1].End;
        double timeUntil = segment.Start - time;
        int? countdown = gapBefore >= COUNTDOWN_GAP_THRESHOLD && timeUntil is > 0 and <= COUNTDOWN_DURATION
            ? (int)Math.Ceiling(timeUntil)
            : null;
        double nextStart = index + 1 < segments.Count ? segments[index + 1].Start : double.PositiveInfinity;
        bool bridgeShortGap = time > segment.End + SEGMENT_LINGER && nextStart - segment.End < COUNTDOWN_GAP_THRESHOLD;
        bool showUpcoming = time < segment.Start - LYRICS_LEAD;
        bool visible = active || countdown != null || bridgeShortGap || showUpcoming;
        bool highlight = active || bridgeShortGap;
        double[] progress = segment.Words.Select(word => WordProgress(word, time, highlight)).ToArray();

        return new UtaLyricsFrame(index, visible, countdown, progress);
    }

    public static double WordProgress(UtaTranscriptWord word, double time, bool active)
    {
        if (!active)
            return 0;

        double start = word.Start - WORD_HIGHLIGHT_LEAD;
        double end = word.End - WORD_HIGHLIGHT_LEAD;
        if (time >= end)
            return 1;
        if (time < start)
            return 0;
        if (end <= start)
            return 1;

        return Math.Clamp((time - start) / (end - start), 0, 1);
    }

    private static UtaTranscriptSegment withEstimatedWords(UtaTranscriptSegment segment)
    {
        string[] tokens = segment.Text.Contains(' ')
            ? segment.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : segment.Text.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
        double duration = Math.Max(0.01, segment.End - segment.Start);

        return new UtaTranscriptSegment
        {
            Text = segment.Text,
            Start = segment.Start,
            End = segment.End,
            Words = tokens.Select((word, index) => new UtaTranscriptWord
            {
                Word = word,
                Start = segment.Start + duration * index / Math.Max(1, tokens.Length),
                End = segment.Start + duration * (index + 1) / Math.Max(1, tokens.Length),
                Estimated = true,
            }).ToArray(),
        };
    }
}

public readonly record struct UtaLyricsFrame(int SegmentIndex, bool Visible, int? Countdown, IReadOnlyList<double> WordProgress);
