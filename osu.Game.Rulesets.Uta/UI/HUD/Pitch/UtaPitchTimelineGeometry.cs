// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Uta.UI.HUD.Pitch;

/// <summary>
/// Gameplay-owned pitch timeline geometry. None of these values are skin configurable.
/// </summary>
public static class UtaPitchTimelineGeometry
{
    public const double LOOK_BEHIND = 1750;
    public const double LOOK_AHEAD = 5250;
    public const float VIEW_SPAN = 19;
    public const float PLAYHEAD_POSITION = (float)(LOOK_BEHIND / (LOOK_BEHIND + LOOK_AHEAD));

    private const double window = LOOK_BEHIND + LOOK_AHEAD;

    public static float TimeToX(double time, double currentTime)
        => (float)((time - currentTime + LOOK_BEHIND) / window);

    public static float MidiToY(float midi, float centreMidi)
        => (centreMidi + VIEW_SPAN / 2 - midi) / VIEW_SPAN;

    public static float TransposeMidi(float midi, float semitones)
        => midi + MathF.Round(semitones);

    public static UtaPitchTargetGeometry Target(double startTime, double endTime, float midi, double currentTime, float centreMidi, float drawWidth)
    {
        float start = TimeToX(startTime, currentTime);
        float end = TimeToX(endTime, currentTime);
        return new UtaPitchTargetGeometry(
            start,
            MidiToY(midi, centreMidi),
            Math.Max(drawWidth > 0 ? 2 / drawWidth : 0, end - start),
            end >= 0 && start <= 1);
    }
}

public readonly record struct UtaPitchTargetGeometry(float X, float Y, float Width, bool Visible);
