// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Karaoke.Scoring.Uta;

public static class UtaPitchMath
{
    private const double exact_pitch_semitones = 0.35;
    private const double good_pitch_semitones = 0.75;
    private const double close_pitch_semitones = 1.5;
    private const double semitone_tolerance = 2.5;

    public static double MidiToFrequency(double midi) => 440 * Math.Pow(2, (midi - 69) / 12);

    public static double FrequencyToMidi(double hertz) => 12 * Math.Log2(hertz / 440) + 69;

    public static double Deviation(double referenceMidi, double userMidi, bool allowOctaveTolerance)
    {
        double difference = userMidi - referenceMidi;
        if (allowOctaveTolerance)
            difference = positiveModulo(difference + 6, 12) - 6;
        return difference;
    }

    public static double Similarity(double referenceHertz, double userHertz, bool allowOctaveTolerance)
    {
        if (!validPitch(referenceHertz) || !validPitch(userHertz))
            return 0;

        double difference = Math.Abs(Deviation(FrequencyToMidi(referenceHertz), FrequencyToMidi(userHertz), allowOctaveTolerance));
        if (difference <= exact_pitch_semitones)
            return 1;
        if (difference <= good_pitch_semitones)
            return 1 - (difference - exact_pitch_semitones) / (good_pitch_semitones - exact_pitch_semitones) * 0.12;
        if (difference <= close_pitch_semitones)
            return 0.88 - (difference - good_pitch_semitones) / (close_pitch_semitones - good_pitch_semitones) * 0.58;

        return Math.Max(0, 0.3 - (difference - close_pitch_semitones) / (semitone_tolerance - close_pitch_semitones) * 0.3);
    }

    public static bool IsFinitePitch(double hertz) => validPitch(hertz);

    private static bool validPitch(double hertz)
        => double.IsFinite(hertz) && hertz is >= UtaPitchDetector.MIN_PITCH_HZ and <= UtaPitchDetector.MAX_PITCH_HZ;

    private static double positiveModulo(double value, double modulus) => (value % modulus + modulus) % modulus;
}
