// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.Uta.Scoring;

internal readonly record struct UtaWeightedCents(int ValueCents, long WeightUnits);
internal readonly record struct UtaPitchObservation(long TimeMicroseconds, int DeviationCents, long WeightUnits);

internal static class UtaScoringMath
{
    private const int exact_pitch_cents = 35;
    private const int good_pitch_cents = 75;
    private const int close_pitch_cents = 150;
    private const int pitch_tolerance_cents = 250;
    private const double stability_scale_cents = 45;

    public static bool IsScorable(UtaScoringTarget target, UtaScoringOptions options)
        => target.Midi.HasValue
           && target.ConfidencePermille >= options.MinimumTargetConfidencePermille
           && KindWeightMultiplier(target.Kind) > 0;

    public static int KindWeightMultiplier(UtaScoringNoteKind kind)
        => kind switch
        {
            UtaScoringNoteKind.Normal => 1,
            UtaScoringNoteKind.Golden => 2,
            _ => 0,
        };

    public static ushort BoundaryWeightPermille(UtaScoringTarget target, long timeMicroseconds, long configuredEdgeMicroseconds)
    {
        long edge = Math.Min(configuredEdgeMicroseconds, Math.Max(0, target.DurationMicroseconds / 4));
        if (edge == 0)
            return UtaScoringOptions.QUALITY_SCALE;

        long fromStart = Math.Clamp(timeMicroseconds - target.StartTimeMicroseconds, 0, edge);
        long fromEnd = Math.Clamp(target.EndTimeMicroseconds - timeMicroseconds, 0, edge);
        long distance = Math.Min(fromStart, fromEnd);
        return checked((ushort)Math.Clamp(RoundDivide(distance * UtaScoringOptions.QUALITY_SCALE, edge), 0, UtaScoringOptions.QUALITY_SCALE));
    }

    public static int DeviationCents(int targetPitchCents, int userPitchCents, bool allowOctaveTolerance)
    {
        int difference = userPitchCents - targetPitchCents;
        if (allowOctaveTolerance)
            difference = positiveModulo(difference + 600, 1200) - 600;
        return difference;
    }

    public static ushort PitchSimilarityPermille(int deviationCents)
    {
        int difference = Math.Abs(deviationCents);
        int result;

        if (difference <= exact_pitch_cents)
            result = 1000;
        else if (difference <= good_pitch_cents)
            result = 1000 - checked((int)RoundDivide((difference - exact_pitch_cents) * 120L, good_pitch_cents - exact_pitch_cents));
        else if (difference <= close_pitch_cents)
            result = 880 - checked((int)RoundDivide((difference - good_pitch_cents) * 580L, close_pitch_cents - good_pitch_cents));
        else
            result = 300 - checked((int)RoundDivide((difference - close_pitch_cents) * 300L, pitch_tolerance_cents - close_pitch_cents));

        return checked((ushort)Math.Clamp(result, 0, UtaScoringOptions.QUALITY_SCALE));
    }

    public static int WeightedMedianCents(IReadOnlyList<UtaWeightedCents> values)
    {
        UtaWeightedCents[] sorted = values.Where(value => value.WeightUnits > 0)
                                                .OrderBy(value => value.ValueCents)
                                                .ToArray();
        if (sorted.Length == 0)
            return 0;

        long total = sorted.Sum(value => value.WeightUnits);
        long cumulative = 0;

        for (int i = 0; i < sorted.Length; i++)
        {
            cumulative = checked(cumulative + sorted[i].WeightUnits);
            long doubled = checked(cumulative * 2);
            if (doubled == total && i + 1 < sorted.Length)
                return checked((int)RoundDivide(sorted[i].ValueCents + (long)sorted[i + 1].ValueCents, 2));
            if (doubled > total)
                return sorted[i].ValueCents;
        }

        return sorted[^1].ValueCents;
    }

    public static ushort RawStabilityPermille(IReadOnlyList<UtaWeightedCents> deviations, int biasCents)
    {
        if (deviations.Count == 0)
            return 0;

        UtaWeightedCents[] residuals = deviations.Select(value => new UtaWeightedCents(Math.Abs(value.ValueCents - biasCents), value.WeightUnits)).ToArray();
        int madCents = WeightedMedianCents(residuals);
        double normalised = madCents / stability_scale_cents;
        return ToPermille(Math.Exp(-normalised * normalised));
    }

    public static ushort LongToneQualityPermille(UtaScoringTarget target, UtaScoringOptions options, ushort coveragePermille, ushort stabilityPermille)
    {
        if (target.DurationMicroseconds < options.MinimumLongToneMicroseconds)
            return 0;

        double coverage = coveragePermille / 1000.0;
        double stability = stabilityPermille / 1000.0;
        return ToPermille(Math.Sqrt(coverage * stability));
    }

    public static UtaProfileRatings Profiles(
        ushort pitchAccuracyPermille,
        ushort effectiveStabilityPermille,
        ushort techniqueQualityPermille,
        bool techniqueAvailable)
    {
        ushort gate = PitchQualityGatePermille(pitchAccuracyPermille);
        int gatedStability = MultiplyQuality(effectiveStabilityPermille, gate);
        int gatedTechnique = MultiplyQuality(techniqueQualityPermille, gate);

        ushort faithful = weightedQuality((pitchAccuracyPermille, 940), (gatedStability, 60));
        ushort stable = weightedQuality((pitchAccuracyPermille, 900), (gatedStability, 100));
        // Notes without an eligible long-tone/vibrato technique section fall
        // back to Faithful for the Technique profile. This keeps short-note
        // passages neutral instead of making a song with many short notes
        // mathematically unable to select the Technique profile.
        ushort technique = techniqueAvailable
            ? weightedQuality((pitchAccuracyPermille, 880), (gatedStability, 60), (gatedTechnique, 60))
            : faithful;

        ushort final = faithful;
        UtaScoringProfile profile = UtaScoringProfile.Faithful;
        if (stable > final)
        {
            final = stable;
            profile = UtaScoringProfile.Stable;
        }

        if (techniqueAvailable && technique > final)
        {
            final = technique;
            profile = UtaScoringProfile.Technique;
        }

        return new UtaProfileRatings(faithful, stable, technique, final, profile);
    }

    public static (UtaNoteGrade Grade, UtaPitchFault Faults) Grade(
        ushort pitchAccuracyPermille,
        ushort coveragePermille,
        ushort hitRatioPermille,
        int biasCents,
        ushort stabilityPermille,
        UtaScoringOptions options)
    {
        UtaNoteGrade grade;
        if (coveragePermille < options.MinimumVoicedPermille)
            grade = UtaNoteGrade.Miss;
        else if (pitchAccuracyPermille >= options.PerfectAccuracyPermille
                 && hitRatioPermille >= options.PerfectHitRatioPermille
                 && coveragePermille >= options.PerfectCoveragePermille)
            grade = UtaNoteGrade.Perfect;
        else if (pitchAccuracyPermille >= options.GreatAccuracyPermille
                 && coveragePermille >= options.GreatCoveragePermille)
            grade = UtaNoteGrade.Great;
        else if (pitchAccuracyPermille >= options.GoodAccuracyPermille
                 && coveragePermille >= options.GoodCoveragePermille)
            grade = UtaNoteGrade.Good;
        else
            grade = UtaNoteGrade.Bad;

        UtaPitchFault faults = UtaPitchFault.None;
        if (coveragePermille < options.GoodCoveragePermille)
            faults |= UtaPitchFault.LowCoverage;
        if (grade is UtaNoteGrade.Bad or UtaNoteGrade.Miss)
        {
            if (biasCents > options.DirectionalBiasCents)
                faults |= UtaPitchFault.High;
            else if (biasCents < -options.DirectionalBiasCents)
                faults |= UtaPitchFault.Low;
        }
        if (stabilityPermille < options.UnstableThresholdPermille)
            faults |= UtaPitchFault.Unstable;
        if (grade is UtaNoteGrade.Bad or UtaNoteGrade.Miss
            && (faults & (UtaPitchFault.High | UtaPitchFault.Low | UtaPitchFault.Unstable | UtaPitchFault.LowCoverage)) == 0)
            faults |= UtaPitchFault.Inaccurate;

        return (grade, faults);
    }

    public static bool IsHit(int deviationCents) => Math.Abs(deviationCents) <= good_pitch_cents;

    public static ushort RatioPermille(long numerator, long denominator)
    {
        if (denominator <= 0)
            return 0;

        return checked((ushort)Math.Clamp(RoundDivide(checked(numerator * UtaScoringOptions.QUALITY_SCALE), denominator), 0, UtaScoringOptions.QUALITY_SCALE));
    }

    public static long MultiplyUnits(long units, int qualityPermille)
        => RoundDivide(checked(units * qualityPermille), UtaScoringOptions.QUALITY_SCALE);

    public static int MultiplyQuality(int leftPermille, int rightPermille)
        => checked((int)Math.Clamp(RoundDivide(leftPermille * (long)rightPermille, UtaScoringOptions.QUALITY_SCALE), 0, UtaScoringOptions.QUALITY_SCALE));

    public static ushort PitchQualityGatePermille(ushort pitchAccuracyPermille)
    {
        const int gate_start = 550;
        const int gate_full = 850;
        if (pitchAccuracyPermille <= gate_start)
            return 0;
        if (pitchAccuracyPermille >= gate_full)
            return UtaScoringOptions.QUALITY_SCALE;

        return checked((ushort)RoundDivide((pitchAccuracyPermille - gate_start) * (long)UtaScoringOptions.QUALITY_SCALE, gate_full - gate_start));
    }

    public static long MaximumUnits(UtaScoringTarget target, UtaScoringOptions options)
    {
        if (!IsScorable(target, options))
            return 0;

        long total = 0;
        long binStart = alignBinStart(target.StartTimeMicroseconds, options.BinDurationMicroseconds);
        while (binStart < target.EndTimeMicroseconds)
        {
            long binEnd = checked(binStart + options.BinDurationMicroseconds);
            total = checked(total + BinMaximumUnits(target, options, binStart, binEnd));
            binStart = binEnd;
        }

        return total;
    }

    public static long BinMaximumUnits(UtaScoringTarget target, UtaScoringOptions options, long binStartMicroseconds, long binEndMicroseconds)
    {
        if (!IsScorable(target, options))
            return 0;

        long overlapStart = Math.Max(target.StartTimeMicroseconds, binStartMicroseconds);
        long overlapEnd = Math.Min(target.EndTimeMicroseconds, binEndMicroseconds);
        if (overlapEnd <= overlapStart)
            return 0;

        long overlapCentre = overlapStart + (overlapEnd - overlapStart) / 2;
        ushort boundary = BoundaryWeightPermille(target, overlapCentre, options.NoteEdgeMicroseconds);
        long units = MultiplyUnits(overlapEnd - overlapStart, boundary);
        units = MultiplyUnits(units, target.ConfidencePermille);
        return checked(units * KindWeightMultiplier(target.Kind));
    }

    public static long AlignBinStart(long timeMicroseconds, long binDurationMicroseconds)
        => alignBinStart(timeMicroseconds, binDurationMicroseconds);

    public static ushort ToPermille(double value)
        => checked((ushort)Math.Clamp(Math.Round(Math.Clamp(value, 0, 1) * UtaScoringOptions.QUALITY_SCALE, MidpointRounding.AwayFromZero), 0, UtaScoringOptions.QUALITY_SCALE));

    public static long RoundDivide(long numerator, long denominator)
    {
        if (denominator <= 0)
            throw new ArgumentOutOfRangeException(nameof(denominator));
        if (numerator >= 0)
            return checked((numerator + denominator / 2) / denominator);

        return checked(-((-numerator + denominator / 2) / denominator));
    }

    private static ushort weightedQuality(params (int Quality, int Weight)[] parts)
    {
        long sum = 0;
        int totalWeight = 0;
        foreach ((int quality, int weight) in parts)
        {
            sum = checked(sum + quality * (long)weight);
            totalWeight = checked(totalWeight + weight);
        }

        return checked((ushort)Math.Clamp(RoundDivide(sum, totalWeight), 0, UtaScoringOptions.QUALITY_SCALE));
    }

    private static long alignBinStart(long timeMicroseconds, long binDurationMicroseconds)
        => timeMicroseconds / binDurationMicroseconds * binDurationMicroseconds;

    private static int positiveModulo(int value, int modulus) => (value % modulus + modulus) % modulus;
}
