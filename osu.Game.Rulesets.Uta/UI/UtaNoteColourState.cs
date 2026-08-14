// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Accumulates a whole target note before committing its colour. The scoring
/// bands and grade order mirror nightingale's pitch state and pitch guide.
/// </summary>
internal sealed class UtaNoteColourState
{
    private const double minimum_scored_duration = 0.04;
    private const float good_pitch_semitones = 0.75f;
    private const float note_pass_ratio = 0.6f;
    private const float note_perfect_ratio = 0.86f;

    private double referenceSeconds;
    private double voicedSeconds;
    private double earned;
    private double hitSeconds;
    private double deviationSeconds;

    public void Accumulate(double elapsedSeconds, bool voiceActive, float similarity, float deviation)
    {
        double elapsed = Math.Clamp(elapsedSeconds, 0, 0.1);
        referenceSeconds += elapsed;

        if (!voiceActive)
            return;

        voicedSeconds += elapsed;
        earned += Math.Clamp(similarity, 0, 1) * elapsed;
        deviationSeconds += deviation * elapsed;
        if (Math.Abs(deviation) <= good_pitch_semitones)
            hitSeconds += elapsed;
    }

    public UtaNoteColourGrade? Grade()
    {
        if (referenceSeconds < minimum_scored_duration)
            return null;

        double hitRatio = Math.Min(1, hitSeconds / referenceSeconds);
        double accuracy = Math.Min(1, earned / referenceSeconds);
        double coverage = Math.Min(1, voicedSeconds / referenceSeconds);
        double deviation = voicedSeconds > 0 ? deviationSeconds / voicedSeconds : 0;

        if (hitRatio >= note_perfect_ratio && accuracy >= 0.94)
            return UtaNoteColourGrade.Perfect;
        if (hitRatio >= note_pass_ratio)
            return UtaNoteColourGrade.Good;
        if (coverage < 0.35 || Math.Abs(deviation) < 0.18)
            return UtaNoteColourGrade.Miss;
        return deviation > 0 ? UtaNoteColourGrade.High : UtaNoteColourGrade.Low;
    }
}

internal enum UtaNoteColourGrade
{
    Perfect,
    Good,
    High,
    Low,
    Miss,
}
