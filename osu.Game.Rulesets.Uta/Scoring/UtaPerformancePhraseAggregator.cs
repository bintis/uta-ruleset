// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Rulesets.Uta.Performance;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// Builds persisted phrase feedback from the same committed note results used by
/// native judgements. A note belongs to the transcript segment containing its
/// temporal centre; phrase metrics are weighted by deterministic note units.
/// </summary>
public static class UtaPerformancePhraseAggregator
{
    private const long missed_interval_merge_gap_microseconds = 100_000;

    public static IReadOnlyList<UtaPerformancePhraseSummary> Aggregate(
        IReadOnlyList<UtaTranscriptSegment> segments,
        IReadOnlyList<UtaNoteScore> notes)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(notes);

        var result = new List<UtaPerformancePhraseSummary>(segments.Count);
        for (int index = 0; index < segments.Count; index++)
        {
            UtaTranscriptSegment segment = segments[index];
            long start = toMicroseconds(segment.Start);
            long end = toMicroseconds(segment.End);
            bool isLastSegment = index == segments.Count - 1;
            UtaNoteScore[] phraseNotes = notes.Where(note =>
                                                   centre(note.Target) >= start
                                                   && (centre(note.Target) < end || isLastSegment && centre(note.Target) <= end))
                                               .OrderBy(note => note.Target.StartTimeMicroseconds)
                                               .ToArray();

            long maximum = sum(phraseNotes, note => note.MaximumUnits);
            long pitch = sum(phraseNotes, note => note.PitchEarnedUnits);
            long voiced = sum(phraseNotes, note => note.VoicedUnits);
            ushort stability = weightedQuality(phraseNotes, note => note.StabilityPermille, note => note.VoicedUnits);
            var biases = phraseNotes.Where(note => note.VoicedUnits > 0)
                                    .Select(note => new UtaWeightedCents(note.BiasCents, note.VoicedUnits))
                                    .ToArray();

            result.Add(new UtaPerformancePhraseSummary
            {
                PhraseIndex = index,
                StartTimeMicroseconds = start,
                EndTimeMicroseconds = end,
                Text = segment.Text,
                PitchAccuracyPermille = UtaScoringMath.RatioPermille(pitch, maximum),
                CoveragePermille = UtaScoringMath.RatioPermille(voiced, maximum),
                StabilityPermille = stability,
                BiasCents = UtaScoringMath.WeightedMedianCents(biases),
                MissedIntervals = missedIntervals(phraseNotes, start, end),
            });
        }

        return result;
    }

    private static IReadOnlyList<UtaMissedInterval> missedIntervals(
        IEnumerable<UtaNoteScore> notes,
        long phraseStart,
        long phraseEnd)
    {
        var source = notes.Where(note => note.Grade == UtaNoteGrade.Miss || note.Faults.HasFlag(UtaPitchFault.LowCoverage))
                          .Select(note => new UtaMissedInterval(
                              Math.Max(phraseStart, note.Target.StartTimeMicroseconds),
                              Math.Min(phraseEnd, note.Target.EndTimeMicroseconds)))
                          .Where(interval => interval.EndTimeMicroseconds > interval.StartTimeMicroseconds)
                          .OrderBy(interval => interval.StartTimeMicroseconds)
                          .ToArray();
        if (source.Length == 0)
            return Array.Empty<UtaMissedInterval>();

        var merged = new List<UtaMissedInterval>();
        long start = source[0].StartTimeMicroseconds;
        long end = source[0].EndTimeMicroseconds;
        for (int i = 1; i < source.Length; i++)
        {
            UtaMissedInterval current = source[i];
            if (current.StartTimeMicroseconds <= end + missed_interval_merge_gap_microseconds)
            {
                end = Math.Max(end, current.EndTimeMicroseconds);
                continue;
            }

            merged.Add(new UtaMissedInterval(start, end));
            start = current.StartTimeMicroseconds;
            end = current.EndTimeMicroseconds;
        }

        merged.Add(new UtaMissedInterval(start, end));
        return merged;
    }

    private static long centre(UtaScoringTarget target)
        => target.StartTimeMicroseconds + target.DurationMicroseconds / 2;

    private static long toMicroseconds(double seconds)
    {
        if (!double.IsFinite(seconds))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        return checked((long)Math.Round(seconds * 1_000_000, MidpointRounding.AwayFromZero));
    }

    private static long sum(IEnumerable<UtaNoteScore> notes, Func<UtaNoteScore, long> selector)
    {
        long result = 0;
        foreach (UtaNoteScore note in notes)
            result = checked(result + selector(note));
        return result;
    }

    private static ushort weightedQuality(
        IEnumerable<UtaNoteScore> notes,
        Func<UtaNoteScore, ushort> quality,
        Func<UtaNoteScore, long> weight)
    {
        long weighted = 0;
        long total = 0;
        foreach (UtaNoteScore note in notes)
        {
            long noteWeight = weight(note);
            if (noteWeight <= 0)
                continue;
            weighted = checked(weighted + noteWeight * quality(note));
            total = checked(total + noteWeight);
        }

        return total > 0
            ? checked((ushort)Math.Clamp(UtaScoringMath.RoundDivide(weighted, total), 0, UtaScoringOptions.QUALITY_SCALE))
            : (ushort)0;
    }
}
