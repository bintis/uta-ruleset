// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Primitives;
using osu.Game.Rulesets.Uta.Configuration;

namespace osu.Game.Rulesets.Uta.UI.HUD;

/// <summary>
/// Computes the complete uta! gameplay HUD layout without inspecting drawables or gameplay state.
/// </summary>
public static class UtaHudLayoutCoordinator
{
    public const float MINIMUM_PITCH_HEIGHT = 140;
    public const float MINIMUM_LYRICS_FONT_SIZE = 22;

    public static UtaHudLayoutSnapshot Calculate(
        float width,
        float height,
        UtaLyricsPosition lyricsPosition = UtaLyricsPosition.Bottom,
        bool showPitch = true,
        bool showLyrics = true,
        bool showScore = true,
        bool showPractice = false,
        bool showRecording = false,
        float additionalSafeAreaPadding = 0)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);

        UtaHudDensity density = DensityForWidth(width);
        float presetPadding = density switch
        {
            UtaHudDensity.Wide => 56,
            UtaHudDensity.Standard => 32,
            UtaHudDensity.Compact => 20,
            _ => 12,
        };
        float safePadding = presetPadding + Math.Clamp(additionalSafeAreaPadding, 0, 64);
        float safeWidth = Math.Max(0, width - safePadding * 2);

        float pitchHeight = density switch
        {
            UtaHudDensity.Wide => 180,
            UtaHudDensity.Standard => 168,
            UtaHudDensity.Compact => 156,
            _ => 144,
        };
        pitchHeight = Math.Clamp(pitchHeight, MINIMUM_PITCH_HEIGHT, Math.Max(MINIMUM_PITCH_HEIGHT, height - safePadding * 2));
        RectangleF pitch = showPitch
            ? new RectangleF(safePadding, 24 + additionalSafeAreaPadding, safeWidth, pitchHeight)
            : RectangleF.Empty;

        float scoreHeight = density == UtaHudDensity.Narrow ? 34 : 38;
        RectangleF score = showScore && showPitch
            ? new RectangleF(pitch.X, pitch.Bottom, pitch.Width, scoreHeight)
            : RectangleF.Empty;
        RectangleF practice = showPractice
            ? new RectangleF(safePadding, Math.Max(safePadding, height - safePadding - 80), Math.Min(280, safeWidth), 80)
            : RectangleF.Empty;
        RectangleF recording = showRecording
            ? new RectangleF(safePadding, safePadding, Math.Min(176, safeWidth), 48)
            : RectangleF.Empty;

        float lyricsWidthFraction = density switch
        {
            UtaHudDensity.Wide => 0.86f,
            UtaHudDensity.Standard => 0.82f,
            UtaHudDensity.Compact => 0.92f,
            _ => 1,
        };
        float lyricsWidth = Math.Min(safeWidth, density == UtaHudDensity.Narrow ? Math.Max(0, safeWidth - 24) : width * lyricsWidthFraction);
        float lyricsHeight = density == UtaHudDensity.Narrow ? 128 : 156;
        float lyricsX = (width - lyricsWidth) / 2;
        float lyricsY = lyricsPosition switch
        {
            UtaLyricsPosition.Top => showPitch ? (score == RectangleF.Empty ? pitch.Bottom : score.Bottom) + 18 : safePadding + 18,
            UtaLyricsPosition.Centre => (height - lyricsHeight) / 2 + 36,
            _ => height - safePadding - lyricsHeight - (showPractice ? practice.Height + 12 : 0),
        };
        lyricsY = Math.Clamp(lyricsY, safePadding, Math.Max(safePadding, height - safePadding - lyricsHeight));
        RectangleF lyrics = showLyrics
            ? new RectangleF(lyricsX, lyricsY, lyricsWidth, lyricsHeight)
            : RectangleF.Empty;

        return new UtaHudLayoutSnapshot(
            pitch,
            lyrics,
            score,
            practice,
            recording,
            density,
            density is UtaHudDensity.Wide or UtaHudDensity.Standard,
            density != UtaHudDensity.Narrow,
            density is UtaHudDensity.Wide or UtaHudDensity.Standard,
            safePadding);
    }

    public static UtaHudDensity DensityForWidth(float width) => width switch
    {
        >= 1280 => UtaHudDensity.Wide,
        >= 840 => UtaHudDensity.Standard,
        >= 560 => UtaHudDensity.Compact,
        _ => UtaHudDensity.Narrow,
    };
}
