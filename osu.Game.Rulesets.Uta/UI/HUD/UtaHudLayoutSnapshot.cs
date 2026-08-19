// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics.Primitives;

namespace osu.Game.Rulesets.Uta.UI.HUD;

public enum UtaHudDensity
{
    Wide,
    Standard,
    Compact,
    Narrow,
}

/// <summary>
/// Immutable output of <see cref="UtaHudLayoutCoordinator"/>. Renderers consume these bounds
/// instead of discovering and avoiding sibling drawables on the gameplay hot path.
/// </summary>
public readonly record struct UtaHudLayoutSnapshot(
    RectangleF PitchBounds,
    RectangleF LyricsBounds,
    RectangleF ScoreBounds,
    RectangleF PracticeBounds,
    RectangleF RecordingBounds,
    UtaHudDensity Density,
    bool ShowMidiLabels,
    bool ShowUpcomingLyrics,
    bool ShowReading,
    float SafeAreaPadding);
