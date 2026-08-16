// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.Uta.Scoring;

public sealed class UtaScoringEngine
{
    private readonly UtaScoringOptions options;

    public UtaScoringEngine(UtaScoringOptions? options = null)
    {
        this.options = options ?? new UtaScoringOptions();
        this.options.Validate();
    }

    public UtaPerformanceScore Score(IEnumerable<UtaScoringTarget> targets, IEnumerable<UtaScoringFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(frames);

        UtaScoringTarget[] orderedTargets = targets.OrderBy(target => target.StartTimeMicroseconds)
                                                   .ThenBy(target => target.Index)
                                                   .ToArray();
        ValidateTargets(orderedTargets);

        UtaScoringFrame[] frameArray = frames.ToArray();
        var resampler = new UtaPitchFrameResampler(frameArray, options);
        var accumulators = new List<UtaNoteScoreAccumulator>(orderedTargets.Length);
        var noteScores = new List<UtaNoteScore>(orderedTargets.Length);

        foreach (UtaScoringTarget target in orderedTargets)
        {
            var accumulator = new UtaNoteScoreAccumulator(target, options);
            accumulators.Add(accumulator);

            long binStart = UtaScoringMath.AlignBinStart(target.StartTimeMicroseconds, options.BinDurationMicroseconds);
            while (binStart < target.EndTimeMicroseconds)
            {
                long binEnd = checked(binStart + options.BinDurationMicroseconds);
                long overlapStart = Math.Max(target.StartTimeMicroseconds, binStart);
                long overlapEnd = Math.Min(target.EndTimeMicroseconds, binEnd);
                long sampleTime = overlapStart + (overlapEnd - overlapStart) / 2;
                accumulator.Accumulate(binStart, binEnd, sampleTime, resampler.SampleAt(sampleTime));
                binStart = binEnd;
            }

            noteScores.Add(accumulator.Complete());
        }

        long maximumUnits = sum(noteScores, note => note.MaximumUnits);
        long pitchEarnedUnits = sum(noteScores, note => note.PitchEarnedUnits);
        long voicedUnits = sum(noteScores, note => note.VoicedUnits);
        long hitUnits = sum(noteScores, note => note.HitUnits);
        long faithfulEarnedUnits = sum(noteScores, note => note.FaithfulEarnedUnits);
        long stableEarnedUnits = sum(noteScores, note => note.StableEarnedUnits);
        long techniqueEarnedUnits = sum(noteScores, note => note.TechniqueEarnedUnits);

        ushort pitchAccuracy = UtaScoringMath.RatioPermille(pitchEarnedUnits, maximumUnits);
        ushort coverage = UtaScoringMath.RatioPermille(voicedUnits, maximumUnits);
        ushort hitRatio = UtaScoringMath.RatioPermille(hitUnits, maximumUnits);
        int biasCents = UtaScoringMath.WeightedMedianCents(accumulators.SelectMany(accumulator => accumulator.Deviations).ToArray());
        ushort rawStability = weightedQuality(noteScores, note => note.RawStabilityPermille, note => note.VoicedUnits);
        ushort stability = weightedQuality(noteScores, note => note.StabilityPermille, note => note.VoicedUnits);
        ushort longToneQuality = weightedQuality(
            noteScores.Where(note => note.Target.DurationMicroseconds >= options.MinimumLongToneMicroseconds),
            note => note.LongToneQualityPermille,
            note => note.MaximumUnits);
        ushort vibratoQuality = weightedQuality(
            noteScores.Where(note => note.Vibrato.Detected),
            note => note.Vibrato.QualityPermille,
            note => note.VoicedUnits);
        ushort techniqueQuality = longToneQuality >= vibratoQuality ? longToneQuality : vibratoQuality;

        ushort faithfulRating = UtaScoringMath.RatioPermille(faithfulEarnedUnits, maximumUnits);
        ushort stableRating = UtaScoringMath.RatioPermille(stableEarnedUnits, maximumUnits);
        ushort techniqueRating = UtaScoringMath.RatioPermille(techniqueEarnedUnits, maximumUnits);
        UtaScoringProfile profile = UtaScoringProfile.Faithful;
        ushort finalRating = faithfulRating;
        long finalEarnedUnits = faithfulEarnedUnits;

        if (stableEarnedUnits > finalEarnedUnits)
        {
            profile = UtaScoringProfile.Stable;
            finalRating = stableRating;
            finalEarnedUnits = stableEarnedUnits;
        }

        if (techniqueEarnedUnits > finalEarnedUnits)
        {
            profile = UtaScoringProfile.Technique;
            finalRating = techniqueRating;
            finalEarnedUnits = techniqueEarnedUnits;
        }

        long totalScore = maximumUnits > 0
            ? Math.Clamp(UtaScoringMath.RoundDivide(checked(finalEarnedUnits * UtaScoringOptions.MAX_SCORE), maximumUnits), 0, UtaScoringOptions.MAX_SCORE)
            : 0;
        (int highestCombo, int highestAccurateStreak) = calculateStreaks(noteScores);
        IReadOnlyDictionary<UtaNoteGrade, int> gradeCounts = Enum.GetValues<UtaNoteGrade>().ToDictionary(grade => grade, grade => noteScores.Count(note => note.Grade == grade));
        IReadOnlyDictionary<UtaPitchFault, int> faultCounts = countFaults(noteScores);

        var preliminary = new UtaPerformanceScore
        {
            Notes = noteScores,
            MaximumUnits = maximumUnits,
            PitchEarnedUnits = pitchEarnedUnits,
            VoicedUnits = voicedUnits,
            HitUnits = hitUnits,
            FaithfulEarnedUnits = faithfulEarnedUnits,
            StableEarnedUnits = stableEarnedUnits,
            TechniqueEarnedUnits = techniqueEarnedUnits,
            PitchAccuracyPermille = pitchAccuracy,
            CoveragePermille = coverage,
            HitRatioPermille = hitRatio,
            BiasCents = biasCents,
            RawStabilityPermille = rawStability,
            StabilityPermille = stability,
            LongToneQualityPermille = longToneQuality,
            VibratoQualityPermille = vibratoQuality,
            TechniqueQualityPermille = techniqueQuality,
            Profiles = new UtaProfileRatings(faithfulRating, stableRating, techniqueRating, finalRating, profile),
            TotalScore = totalScore,
            HighestCombo = highestCombo,
            HighestAccurateStreak = highestAccurateStreak,
            GradeCounts = gradeCounts,
            FaultCounts = faultCounts,
            Analysis = new UtaAnalysisReport(UtaAnalysisMessage.None, UtaAnalysisMessage.None),
        };

        return preliminary with { Analysis = UtaAnalysisReportGenerator.Generate(preliminary) };
    }

    public UtaNoteScore ScoreNote(UtaScoringTarget target, IEnumerable<UtaScoringFrame> frames)
        => Score(new[] { target }, frames).Notes.Single();

    internal void ValidateTargets(IReadOnlyList<UtaScoringTarget> targets)
    {
        UtaScoringTarget? previousScorable = null;
        var indices = new HashSet<int>();

        foreach (UtaScoringTarget target in targets)
        {
            if (target.Index < 0)
                throw new ArgumentException("A scoring target has a negative index.", nameof(targets));
            if (!indices.Add(target.Index))
                throw new ArgumentException($"Scoring target index {target.Index} is used more than once.", nameof(targets));
            if (target.StartTimeMicroseconds < 0 || target.EndTimeMicroseconds <= target.StartTimeMicroseconds)
                throw new ArgumentException($"Scoring target {target.Index} has an invalid interval.", nameof(targets));
            if (target.Midi is < 0 or > 127)
                throw new ArgumentException($"Scoring target {target.Index} has an invalid MIDI value.", nameof(targets));
            if (target.ConfidencePermille > UtaScoringOptions.QUALITY_SCALE)
                throw new ArgumentException($"Scoring target {target.Index} has invalid confidence.", nameof(targets));

            if (target.Midi is { } midi)
            {
                int adjustedMidi = midi + options.TransposeSemitones;
                if (adjustedMidi is < 0 or > 127)
                    throw new ArgumentException($"Scoring target {target.Index} is outside MIDI 0-127 after Transpose.", nameof(targets));
            }

            if (!UtaScoringMath.IsScorable(target, options))
                continue;

            if (previousScorable is { } previous && target.StartTimeMicroseconds < previous.EndTimeMicroseconds)
                throw new ArgumentException($"Scorable targets {previous.Index} and {target.Index} overlap.", nameof(targets));

            previousScorable = target;
        }
    }

    private static long sum(IEnumerable<UtaNoteScore> notes, Func<UtaNoteScore, long> value)
    {
        long result = 0;
        foreach (UtaNoteScore note in notes)
            result = checked(result + value(note));
        return result;
    }

    private static ushort weightedQuality(IEnumerable<UtaNoteScore> notes, Func<UtaNoteScore, ushort> quality, Func<UtaNoteScore, long> weight)
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

    private static (int HighestCombo, int HighestAccurateStreak) calculateStreaks(IEnumerable<UtaNoteScore> notes)
    {
        int combo = 0;
        int highestCombo = 0;
        int accurate = 0;
        int highestAccurate = 0;

        foreach (UtaNoteScore note in notes)
        {
            if (note.Grade == UtaNoteGrade.Ignored)
                continue;

            if (UtaNativeResultMapper.ContinuesNativeCombo(note.Grade))
            {
                combo++;
                highestCombo = Math.Max(highestCombo, combo);
            }
            else
                combo = 0;

            if (UtaNativeResultMapper.ContinuesAccurateStreak(note.Grade))
            {
                accurate++;
                highestAccurate = Math.Max(highestAccurate, accurate);
            }
            else
                accurate = 0;
        }

        return (highestCombo, highestAccurate);
    }

    private static IReadOnlyDictionary<UtaPitchFault, int> countFaults(IEnumerable<UtaNoteScore> notes)
    {
        UtaPitchFault[] individual =
        {
            UtaPitchFault.High,
            UtaPitchFault.Low,
            UtaPitchFault.Unstable,
            UtaPitchFault.Inaccurate,
            UtaPitchFault.LowCoverage,
        };
        return individual.ToDictionary(fault => fault, fault => notes.Count(note => note.Faults.HasFlag(fault)));
    }
}
