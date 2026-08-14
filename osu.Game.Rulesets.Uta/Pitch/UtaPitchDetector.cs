// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Buffers;

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

        double[]? rentedCentred = null;
        Span<double> centred = samples.Length <= 4096
            ? stackalloc double[samples.Length]
            : (rentedCentred = ArrayPool<double>.Shared.Rent(samples.Length)).AsSpan(0, samples.Length);

        double squareSum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double value = samples[i] - mean;
            centred[i] = value;
            squareSum += value * value;
        }

        if (Math.Sqrt(squareSum / samples.Length) < RMS_GATE)
        {
            if (rentedCentred != null)
                ArrayPool<double>.Shared.Return(rentedCentred);
            return null;
        }

        int minLag = Math.Max(2, (int)Math.Floor(sampleRate / MAX_PITCH_HZ));
        int maxLag = Math.Min(samples.Length / 2, (int)Math.Ceiling(sampleRate / MIN_PITCH_HZ));
        if (minLag >= maxLag)
        {
            if (rentedCentred != null)
                ArrayPool<double>.Shared.Return(rentedCentred);
            return null;
        }

        double[]? rentedCorrelations = null;
        Span<double> correlations = maxLag + 1 <= 2048
            ? stackalloc double[maxLag + 1]
            : (rentedCorrelations = ArrayPool<double>.Shared.Rent(maxLag + 1)).AsSpan(0, maxLag + 1);

        try
        {
            double leftEnergy = 0;
            double rightEnergy = 0;
            int initialCount = samples.Length - minLag;
            for (int i = 0; i < initialCount; i++)
                leftEnergy += centred[i] * centred[i];
            for (int i = minLag; i < samples.Length; i++)
                rightEnergy += centred[i] * centred[i];

            correlations[minLag - 1] = double.NegativeInfinity;
            double best = 0;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                int count = samples.Length - lag;
                double product = 0;

                for (int i = 0; i < count; i++)
                    product += centred[i] * centred[i + lag];

                correlations[lag] = product / Math.Max(double.Epsilon, Math.Sqrt(Math.Max(0, leftEnergy) * Math.Max(0, rightEnergy)));
                best = Math.Max(best, correlations[lag]);

                if (lag < maxLag)
                {
                    leftEnergy -= centred[count - 1] * centred[count - 1];
                    rightEnergy -= centred[lag] * centred[lag];
                }
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
        finally
        {
            if (rentedCorrelations != null)
                ArrayPool<double>.Shared.Return(rentedCorrelations);
            if (rentedCentred != null)
                ArrayPool<double>.Shared.Return(rentedCentred);
        }
    }
}
