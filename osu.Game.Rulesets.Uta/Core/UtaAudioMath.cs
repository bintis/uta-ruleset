// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;

namespace osu.Game.Rulesets.Uta.Core;

/// <summary>
/// Pure helpers for the TrackBass-backed playback graph. Device I/O stays in
/// <see cref="UtaAudioDevices"/> / <see cref="UtaAudioRouter"/>.
/// </summary>
internal static class UtaAudioMath
{
    public const float DefaultVocalsVolume = 0.55f;
    public const double DriftThresholdMs = 25;
    public const float LatencyRouteThresholdMs = 0.5f;

    public static (double Frequency, double Tempo) TransposeFactors(int semitones)
    {
        if (semitones == 0)
            return (1, 1);

        double frequency = Math.Pow(2, semitones / 12.0);
        return (frequency, 1 / frequency);
    }

    public static float EffectiveVocalsVolume(bool enabled, float slider)
        => enabled ? slider : 0;

    /// <summary>
    /// VOX turns original vocals on. An empty constructor after 切歌 is not an
    /// explicit off — keep the last preferred state (AUDIO leftover doc §24).
    /// </summary>
    public static bool OriginalVocalsShouldPlay(bool fromMods, bool preferred)
        => fromMods || preferred;

    public static bool NeedsRoutedBgm(bool customOutput, float latencyMs)
        => customOutput || Math.Abs(latencyMs) >= LatencyRouteThresholdMs;

    /// <summary>
    /// Native VOX on osu's mixer is a different BASS graph from routed BGM.
    /// After leftover halt/DestroyBuses that native track is created on the
    /// leaked mixer device and is silent. Follow the BGM route when BGM is
    /// already off TrackBass.
    /// </summary>
    public static bool NeedsRoutedVocals(bool customOutput, bool bgmIsRouted)
        => customOutput || bgmIsRouted;

    public static bool NeedsRoutedOutput(string? requested, int defaultDevice)
        => !string.IsNullOrWhiteSpace(requested) && UtaAudioDevices.Resolve(requested) != defaultDevice;

    public static bool DriftNeedsCorrection(double expectedMs, double actualMs, double thresholdMs = DriftThresholdMs)
        => Math.Abs(actualMs - expectedMs) > thresholdMs;
}
