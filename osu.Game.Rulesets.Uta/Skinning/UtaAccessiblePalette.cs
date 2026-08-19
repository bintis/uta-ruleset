// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.Skinning;

internal static class UtaAccessiblePalette
{
    public static readonly Color4 Background = new(10, 12, 22, 255);
    public static readonly Color4 Grid = new(75, 82, 111, 130);
    public static readonly Color4 Target = new(130, 199, 255, 255);
    public static readonly Color4 SongCurve = new(100, 183, 255, 255);
    public static readonly Color4 LiveCurve = new(111, 231, 166, 255);
    public static readonly Color4 Playhead = Color4.White;
    public static readonly Color4 Good = new(89, 211, 154, 255);
    public static readonly Color4 Bad = new(240, 109, 124, 255);

    /// <summary>
    /// Blends an unsafe custom colour towards black or white until it reaches
    /// the requested WCAG-style contrast ratio. Hue is retained as far as possible.
    /// </summary>
    public static Color4 EnsureContrast(Color4 foreground, Color4 background, double minimumRatio = 3)
    {
        if (contrast(foreground, background) >= minimumRatio)
            return foreground;

        Color4 black = new(0f, 0f, 0f, foreground.A);
        Color4 white = new(1f, 1f, 1f, foreground.A);
        Color4 target = contrast(black, background) > contrast(white, background) ? black : white;
        for (int step = 1; step <= 20; step++)
        {
            float amount = step / 20f;
            var candidate = new Color4(
                foreground.R + (target.R - foreground.R) * amount,
                foreground.G + (target.G - foreground.G) * amount,
                foreground.B + (target.B - foreground.B) * amount,
                foreground.A);
            if (contrast(candidate, background) >= minimumRatio)
                return candidate;
        }

        return target;
    }

    private static double contrast(Color4 first, Color4 second)
    {
        double a = luminance(first);
        double b = luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double luminance(Color4 colour)
        => 0.2126 * linear(colour.R) + 0.7152 * linear(colour.G) + 0.0722 * linear(colour.B);

    private static double linear(float value)
    {
        double channel = Math.Clamp(value, 0, 1);
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
