// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Uta.Pitch;

public static class UtaPitchDetector
{
    public const double MIN_PITCH_HZ = 80;
    public const double MAX_PITCH_HZ = 1000;
    public const double RMS_GATE = 0.003;

    /// <summary>
    /// Detects a monophonic pitch with the normalized-autocorrelation algorithm used by Uta.
    /// </summary>
    public static double? Detect(ReadOnlySpan<float> samples, double sampleRate)
    {
        if (samples.Length < 256 || !double.IsFinite(sampleRate) || sampleRate <= 0)
            return null;

        double mean = 0;
        foreach (float sample in samples)
            mean += sample;
        mean /= samples.Length;

        double squareSum = 0;
        foreach (float sample in samples)
        {
            double centered = sample - mean;
            squareSum += centered * centered;
        }

        if (Math.Sqrt(squareSum / samples.Length) < RMS_GATE)
            return null;

        int minLag = Math.Max(2, (int)Math.Floor(sampleRate / MAX_PITCH_HZ));
        int maxLag = Math.Min(samples.Length / 2, (int)Math.Ceiling(sampleRate / MIN_PITCH_HZ));
        if (minLag >= maxLag)
            return null;

        double[] correlations = new double[maxLag + 1];
        double best = 0;

        for (int lag = minLag; lag <= maxLag; lag++)
        {
            int count = samples.Length - lag;
            double product = 0;
            double leftEnergy = 0;
            double rightEnergy = 0;

            for (int i = 0; i < count; i++)
            {
                double left = samples[i] - mean;
                double right = samples[i + lag] - mean;
                product += left * right;
                leftEnergy += left * left;
                rightEnergy += right * right;
            }

            correlations[lag] = product / Math.Max(double.Epsilon, Math.Sqrt(leftEnergy * rightEnergy));
            best = Math.Max(best, correlations[lag]);
        }

        if (best < 0.55)
            return null;

        double threshold = Math.Max(best * 0.9, 0.58);
        int peak = 0;

        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double right = lag < maxLag ? correlations[lag + 1] : -1;
            if (correlations[lag] >= threshold && correlations[lag] >= correlations[lag - 1] && correlations[lag] >= right)
            {
                peak = lag;
                break;
            }
        }

        if (peak == 0)
        {
            peak = minLag;
            for (int lag = minLag + 1; lag <= maxLag; lag++)
            {
                if (correlations[lag] > correlations[peak])
                    peak = lag;
            }
        }

        peak = Math.Clamp(peak, 1, maxLag - 1);
        double leftCorrelation = correlations[peak - 1];
        double centreCorrelation = correlations[peak];
        double rightCorrelation = correlations[peak + 1];
        double denominator = leftCorrelation - 2 * centreCorrelation + rightCorrelation;
        double adjustment = Math.Abs(denominator) > 1e-9
            ? 0.5 * (leftCorrelation - rightCorrelation) / denominator
            : 0;
        double hertz = sampleRate / (peak + Math.Clamp(adjustment, -0.5, 0.5));

        return hertz is >= MIN_PITCH_HZ and <= MAX_PITCH_HZ ? hertz : null;
    }
}
