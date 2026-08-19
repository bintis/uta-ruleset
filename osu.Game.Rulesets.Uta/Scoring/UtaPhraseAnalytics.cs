// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Uta.Formats;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// Deterministically derives phrase-level result data from the same immutable
/// <see cref="UtaNoteScore"/> values that drive the native lazer score.
/// No score is recomputed in the results UI.
/// </summary>
public static class UtaPhraseAnalytics
{
    public static IReadOnlyList<UtaPhraseScore> Analyse(
        IReadOnlyList<UtaNoteScore> notes,
        IReadOnlyList<UtaTranscriptSegment> transcript)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(transcript);

        if (transcript.Count == 0)
            return Array.Empty<UtaPhraseScore>();

        var result = new List<UtaPhraseScore>(transcript.Count);
        for (int i = 0; i < transcript.Count; i++)
            result.Add(AnalysePhrase(notes, transcript[i], i));

        return result;
    }

    public static UtaPhraseScore AnalysePhrase(
        IReadOnlyList<UtaNoteScore> notes,
        UtaTranscriptSegment segment,
        int phraseIndex)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(segment);

        long start = checked((long)Math.Round(segment.Start * 1_000_000, MidpointRounding.AwayFromZero));
        long end = checked((long)Math.Round(segment.End * 1_000_000, MidpointRounding.AwayFromZero));

        UtaNoteScore[] phraseNotes = notes.Where(note =>
        {
            long centre = note.Target.StartTimeMicroseconds
                          + (note.Target.EndTimeMicroseconds - note.Target.StartTimeMicroseconds) / 2;
            return centre >= start && centre < end;
        }).ToArray();

        if (phraseNotes.Length == 0)
        {
            return new UtaPhraseScore(
                phraseIndex, start, end, segment.Text ?? string.Empty,
                0, 0, 0, 0, 0, Array.Empty<UtaMissedSection>());
        }

        long maximum = phraseNotes.Sum(n => n.MaximumUnits);
        ushort pitch = weightedPermille(phraseNotes, n => n.PitchEarnedUnits, maximum);
        ushort coverage = weightedPermille(phraseNotes, n => n.VoicedUnits, maximum);
        ushort stability = weightedQuality(phraseNotes, n => n.StabilityPermille);
        int bias = weightedBias(phraseNotes);
        ushort rating = weightedQuality(phraseNotes, n => n.Profiles.FinalPermille);

        UtaMissedSection[] missed = phraseNotes
            .Where(n => n.Grade == UtaNoteGrade.Miss)
            .Select(n => new UtaMissedSection(n.Target.StartTimeMicroseconds, n.Target.EndTimeMicroseconds))
            .ToArray();

        return new UtaPhraseScore(
            phraseIndex, start, end, segment.Text ?? string.Empty,
            pitch, coverage, stability, rating, bias, missed);
    }

    private static ushort weightedPermille(UtaNoteScore[] notes, Func<UtaNoteScore, long> numerator, long denominator)
    {
        if (denominator <= 0)
            return 0;

        long value = notes.Sum(numerator);
        return checked((ushort)Math.Clamp(
            Math.Round(value * (double)UtaScoringOptions.QUALITY_SCALE / denominator, MidpointRounding.AwayFromZero),
            0, UtaScoringOptions.QUALITY_SCALE));
    }

    private static ushort weightedQuality(UtaNoteScore[] notes, Func<UtaNoteScore, ushort> selector)
    {
        long denominator = notes.Sum(n => n.MaximumUnits);
        if (denominator <= 0)
            return 0;

        double numerator = notes.Sum(n => n.MaximumUnits * (double)selector(n));
        return checked((ushort)Math.Clamp(
            Math.Round(numerator / denominator, MidpointRounding.AwayFromZero),
            0, UtaScoringOptions.QUALITY_SCALE));
    }

    private static int weightedBias(UtaNoteScore[] notes)
    {
        long denominator = notes.Sum(n => n.MaximumUnits);
        if (denominator <= 0)
            return 0;

        double numerator = notes.Sum(n => n.MaximumUnits * (double)n.BiasCents);
        return checked((int)Math.Round(numerator / denominator, MidpointRounding.AwayFromZero));
    }
}

public readonly record struct UtaPhraseScore(
    int PhraseIndex,
    long StartTimeMicroseconds,
    long EndTimeMicroseconds,
    string Text,
    ushort PitchAccuracyPermille,
    ushort CoveragePermille,
    ushort StabilityPermille,
    ushort OverallPermille,
    int BiasCents,
    IReadOnlyList<UtaMissedSection> MissedSections);

public readonly record struct UtaMissedSection(long StartTimeMicroseconds, long EndTimeMicroseconds);
