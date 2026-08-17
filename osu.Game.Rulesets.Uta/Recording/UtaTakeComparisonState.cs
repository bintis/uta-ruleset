// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Uta.Recording;

/// <summary>
/// Pure state used by the native results statistic panel for take/original-vocal
/// A/B comparison. Audio routing consumes this state; the UI does not own a
/// second gameplay/result screen.
/// </summary>
public sealed class UtaTakeComparisonState
{
    public UtaComparisonSide Side { get; private set; } = UtaComparisonSide.PlayerTake;
    public double PositionMilliseconds { get; private set; }
    public bool Playing { get; private set; }
    public double BackgroundMusicVolume { get; set; } = 1;
    public double PlayerTakeVolume { get; set; } = 1;
    public double OriginalVocalVolume { get; set; } = 1;

    public void Seek(double positionMilliseconds)
    {
        if (!double.IsFinite(positionMilliseconds) || positionMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(positionMilliseconds));
        PositionMilliseconds = positionMilliseconds;
    }

    public void SetPlaying(bool playing) => Playing = playing;

    public void Select(UtaComparisonSide side) => Side = side;

    public void Toggle()
        => Side = Side == UtaComparisonSide.PlayerTake
            ? UtaComparisonSide.OriginalVocal
            : UtaComparisonSide.PlayerTake;

    public UtaComparisonMix GetMix(double crossfade)
    {
        crossfade = Math.Clamp(crossfade, 0, 1);
        double active = crossfade;
        double inactive = 1 - crossfade;

        return Side == UtaComparisonSide.PlayerTake
            ? new UtaComparisonMix(BackgroundMusicVolume, PlayerTakeVolume * active, OriginalVocalVolume * inactive)
            : new UtaComparisonMix(BackgroundMusicVolume, PlayerTakeVolume * inactive, OriginalVocalVolume * active);
    }
}

public enum UtaComparisonSide
{
    PlayerTake,
    OriginalVocal,
}

public readonly record struct UtaComparisonMix(double BackgroundMusic, double PlayerTake, double OriginalVocal);
