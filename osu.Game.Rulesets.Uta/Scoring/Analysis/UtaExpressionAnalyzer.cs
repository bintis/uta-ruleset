// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// Report-only RMS analysis. The result is intentionally not included in the
/// v2 score because microphone gain, distance, AGC and compression can change
/// loudness independently of singing quality.
/// </summary>
public static class UtaExpressionAnalyzer
{
    public static UtaExpressionAnalysis Analyse(IEnumerable<UtaExpressionFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        UtaExpressionFrame[] source = frames.ToArray();
        foreach (UtaExpressionFrame frame in source)
        {
            if (frame.TimeMicroseconds < 0)
                throw new ArgumentException("Expression frames cannot have negative time.", nameof(frames));
            if (frame.RmsDecibelsTenths is < -1_200 or > 120)
                throw new ArgumentException("Expression RMS is outside -120.0 to +12.0 dB.", nameof(frames));
            if (frame.PeakPermille > 1000)
                throw new ArgumentException("Expression peak is outside 0-1000.", nameof(frames));
        }

        UtaExpressionFrame[] voiced = source.Where(frame => frame.Voiced && frame.RmsDecibelsTenths > -900)
                                            .OrderBy(frame => frame.TimeMicroseconds)
                                            .ToArray();
        if (voiced.Length == 0)
            return default;

        short[] levels = voiced.Select(frame => frame.RmsDecibelsTenths).Order().ToArray();
        short p10 = percentile(levels, 0.10);
        short p50 = percentile(levels, 0.50);
        short p90 = percentile(levels, 0.90);
        int dynamicRange = p90 - p10;
        int clipped = voiced.Count(frame => frame.PeakPermille >= 995);
        ushort clippingRatio = checked((ushort)Math.Round(clipped * 1000.0 / voiced.Length, MidpointRounding.AwayFromZero));
        bool possibleAutomaticGainControl = dynamicRange < 25 && voiced.Length >= 50;

        return new UtaExpressionAnalysis(
            true,
            p10,
            p50,
            p90,
            checked((short)dynamicRange),
            clippingRatio,
            possibleAutomaticGainControl);
    }

    private static short percentile(IReadOnlyList<short> values, double percentile)
    {
        int index = (int)Math.Round((values.Count - 1) * percentile, MidpointRounding.AwayFromZero);
        return values[Math.Clamp(index, 0, values.Count - 1)];
    }
}

public readonly record struct UtaExpressionFrame(
    long TimeMicroseconds,
    short RmsDecibelsTenths,
    ushort PeakPermille,
    bool Voiced);

public readonly record struct UtaExpressionAnalysis(
    bool Available,
    short P10DecibelsTenths,
    short MedianDecibelsTenths,
    short P90DecibelsTenths,
    short DynamicRangeDecibelsTenths,
    ushort ClippingRatioPermille,
    bool PossibleAutomaticGainControl);
