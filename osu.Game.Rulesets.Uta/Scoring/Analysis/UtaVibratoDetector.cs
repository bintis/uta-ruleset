// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.Uta.Scoring;

internal static class UtaVibratoDetector
{
    private const double minimum_correlation = 0.55;
    private const int minimum_extent_cents = 15;
    private const int maximum_extent_cents = 100;
    private const double maximum_centre_drift_cents_per_second = 80;

    public static UtaVibratoResult Analyse(IReadOnlyList<UtaPitchObservation> observations, UtaScoringOptions options)
    {
        if (observations.Count < 4)
            return default;

        UtaPitchObservation[] run = longestContiguousRun(observations, options.BinDurationMicroseconds);
        if (run.Length < 4)
            return default;

        long duration = run[^1].TimeMicroseconds - run[0].TimeMicroseconds + options.BinDurationMicroseconds;
        if (duration < options.MinimumVibratoMicroseconds)
            return default;

        (double intercept, double slopePerBin) = linearTrend(run);
        double driftPerSecond = slopePerBin * 1_000_000 / options.BinDurationMicroseconds;
        if (Math.Abs(driftPerSecond) > maximum_centre_drift_cents_per_second)
            return default;

        double[] detrended = run.Select((value, index) => value.DeviationCents - (intercept + slopePerBin * index)).ToArray();
        double rms = Math.Sqrt(detrended.Sum(value => value * value) / detrended.Length);
        int extent = checked((int)Math.Round(rms * Math.Sqrt(2), MidpointRounding.AwayFromZero));
        if (extent < minimum_extent_cents || extent > maximum_extent_cents)
            return default;

        int minimumLag = Math.Max(2, (int)Math.Floor(1_000_000.0 / (10 * options.BinDurationMicroseconds)));
        int maximumLag = Math.Max(minimumLag, (int)Math.Ceiling(1_000_000.0 / (3.5 * options.BinDurationMicroseconds)));
        maximumLag = Math.Min(maximumLag, detrended.Length / 2);
        if (minimumLag > maximumLag)
            return default;

        int bestLag = 0;
        double bestCorrelation = 0;
        for (int lag = minimumLag; lag <= maximumLag; lag++)
        {
            double correlation = normalisedCorrelation(detrended, lag);
            if (correlation > bestCorrelation)
            {
                bestCorrelation = correlation;
                bestLag = lag;
            }
        }

        if (bestLag == 0 || bestCorrelation < minimum_correlation)
            return default;

        double rate = 1_000_000.0 / (bestLag * options.BinDurationMicroseconds);
        double cycles = duration / 1_000_000.0 * rate;
        if (cycles < 2)
            return default;

        double correlationQuality = Math.Clamp((bestCorrelation - minimum_correlation) / (0.92 - minimum_correlation), 0, 1);
        double cycleQuality = Math.Clamp((cycles - 2) / 3, 0, 1);
        double extentQuality = extent <= 55
            ? Math.Clamp((extent - minimum_extent_cents) / 40.0, 0, 1)
            : Math.Clamp((maximum_extent_cents - extent) / 45.0, 0, 1);
        double driftQuality = Math.Clamp((maximum_centre_drift_cents_per_second - Math.Abs(driftPerSecond)) / 60, 0, 1);
        double quality = 0.55 * correlationQuality + 0.20 * cycleQuality + 0.15 * extentQuality + 0.10 * driftQuality;

        return new UtaVibratoResult(
            true,
            UtaScoringMath.ToPermille(quality),
            UtaScoringMath.ToPermille(bestCorrelation),
            rate,
            extent,
            checked((int)Math.Round(driftPerSecond, MidpointRounding.AwayFromZero)),
            duration);
    }

    private static (double Intercept, double SlopePerBin) linearTrend(IReadOnlyList<UtaPitchObservation> values)
    {
        double xMean = (values.Count - 1) / 2.0;
        double yMean = values.Average(value => value.DeviationCents);
        double covariance = 0;
        double variance = 0;
        for (int i = 0; i < values.Count; i++)
        {
            double x = i - xMean;
            covariance += x * (values[i].DeviationCents - yMean);
            variance += x * x;
        }

        double slope = variance > 0 ? covariance / variance : 0;
        return (yMean - slope * xMean, slope);
    }

    private static UtaPitchObservation[] longestContiguousRun(IReadOnlyList<UtaPitchObservation> observations, long binDurationMicroseconds)
    {
        UtaPitchObservation[] ordered = observations.OrderBy(value => value.TimeMicroseconds).ToArray();
        int bestStart = 0;
        int bestLength = 1;
        int currentStart = 0;

        for (int i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].TimeMicroseconds - ordered[i - 1].TimeMicroseconds > binDurationMicroseconds * 3 / 2)
                currentStart = i;

            int currentLength = i - currentStart + 1;
            if (currentLength > bestLength)
            {
                bestStart = currentStart;
                bestLength = currentLength;
            }
        }

        return ordered.Skip(bestStart).Take(bestLength).ToArray();
    }

    private static double normalisedCorrelation(IReadOnlyList<double> values, int lag)
    {
        double product = 0;
        double leftEnergy = 0;
        double rightEnergy = 0;
        int count = values.Count - lag;
        for (int i = 0; i < count; i++)
        {
            double left = values[i];
            double right = values[i + lag];
            product += left * right;
            leftEnergy += left * left;
            rightEnergy += right * right;
        }

        return product / Math.Max(double.Epsilon, Math.Sqrt(leftEnergy * rightEnergy));
    }
}
