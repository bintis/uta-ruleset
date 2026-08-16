// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.Uta.Scoring;

internal sealed class UtaNoteScoreAccumulator
{
    private readonly UtaScoringTarget target;
    private readonly UtaScoringOptions options;
    private readonly List<UtaWeightedCents> deviations = new();
    private readonly List<UtaPitchObservation> observations = new();

    private long maximumUnits;
    private long pitchEarnedUnits;
    private long voicedUnits;
    private long hitUnits;

    public IReadOnlyList<UtaWeightedCents> Deviations => deviations;

    public UtaNoteScoreAccumulator(UtaScoringTarget target, UtaScoringOptions options)
    {
        this.target = target;
        this.options = options;
    }

    public void Accumulate(long binStartMicroseconds, long binEndMicroseconds, long sampleTimeMicroseconds, UtaResampledPitch sample)
    {
        long units = UtaScoringMath.BinMaximumUnits(target, options, binStartMicroseconds, binEndMicroseconds);
        if (units <= 0)
            return;

        maximumUnits = checked(maximumUnits + units);
        if (!sample.Voiced || target.Midi == null)
            return;

        int targetPitchCents = checked((target.Midi.Value + options.TransposeSemitones) * 100);
        int deviationCents = UtaScoringMath.DeviationCents(targetPitchCents, sample.PitchCents, options.AllowOctaveTolerance);
        ushort similarity = UtaScoringMath.PitchSimilarityPermille(deviationCents);

        voicedUnits = checked(voicedUnits + units);
        pitchEarnedUnits = checked(pitchEarnedUnits + UtaScoringMath.MultiplyUnits(units, similarity));
        deviations.Add(new UtaWeightedCents(deviationCents, units));
        if (UtaScoringMath.BoundaryWeightPermille(target, sampleTimeMicroseconds, options.NoteEdgeMicroseconds) >= 500)
            observations.Add(new UtaPitchObservation(sampleTimeMicroseconds, deviationCents, units));
        if (UtaScoringMath.IsHit(deviationCents))
            hitUnits = checked(hitUnits + units);
    }

    public UtaNoteScore Complete()
    {
        if (maximumUnits <= 0)
        {
            return new UtaNoteScore
            {
                Target = target,
                Grade = UtaNoteGrade.Ignored,
                Faults = UtaPitchFault.None,
                Profiles = new UtaProfileRatings(0, 0, 0, 0, UtaScoringProfile.Faithful),
            };
        }

        ushort pitchAccuracy = UtaScoringMath.RatioPermille(pitchEarnedUnits, maximumUnits);
        ushort coverage = UtaScoringMath.RatioPermille(voicedUnits, maximumUnits);
        ushort hitRatio = UtaScoringMath.RatioPermille(hitUnits, maximumUnits);
        int biasCents = UtaScoringMath.WeightedMedianCents(deviations);
        ushort rawStability = UtaScoringMath.RawStabilityPermille(deviations, biasCents);
        UtaVibratoResult vibrato = UtaVibratoDetector.Analyse(observations, options);
        ushort stability = vibrato.Detected && vibrato.QualityPermille > rawStability ? vibrato.QualityPermille : rawStability;
        ushort effectiveStability = checked((ushort)UtaScoringMath.MultiplyQuality(stability, coverage));
        ushort longToneQuality = UtaScoringMath.LongToneQualityPermille(target, options, coverage, stability);
        ushort techniqueQuality = longToneQuality >= vibrato.QualityPermille ? longToneQuality : vibrato.QualityPermille;
        bool techniqueAvailable = target.DurationMicroseconds >= options.MinimumLongToneMicroseconds || vibrato.Detected;
        UtaProfileRatings profiles = UtaScoringMath.Profiles(pitchAccuracy, effectiveStability, techniqueQuality, techniqueAvailable);
        (UtaNoteGrade grade, UtaPitchFault faults) = UtaScoringMath.Grade(pitchAccuracy, coverage, hitRatio, biasCents, stability, options);

        return new UtaNoteScore
        {
            Target = target,
            MaximumUnits = maximumUnits,
            PitchEarnedUnits = pitchEarnedUnits,
            VoicedUnits = voicedUnits,
            HitUnits = hitUnits,
            FaithfulEarnedUnits = UtaScoringMath.MultiplyUnits(maximumUnits, profiles.FaithfulPermille),
            StableEarnedUnits = UtaScoringMath.MultiplyUnits(maximumUnits, profiles.StablePermille),
            TechniqueEarnedUnits = UtaScoringMath.MultiplyUnits(maximumUnits, profiles.TechniquePermille),
            PitchAccuracyPermille = pitchAccuracy,
            CoveragePermille = coverage,
            HitRatioPermille = hitRatio,
            BiasCents = biasCents,
            RawStabilityPermille = rawStability,
            StabilityPermille = stability,
            EffectiveStabilityPermille = effectiveStability,
            LongToneQualityPermille = longToneQuality,
            TechniqueQualityPermille = techniqueQuality,
            Vibrato = vibrato,
            Profiles = profiles,
            Grade = grade,
            Faults = faults,
        };
    }
}
