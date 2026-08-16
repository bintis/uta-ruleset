// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Uta.Pitch;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// Pitch-analysis output before gameplay-time mapping. This value may cross the
/// microphone worker boundary, but only the mapped and quantised
/// <see cref="UtaScoringFrame"/> is part of the deterministic scoring contract.
/// </summary>
public readonly record struct UtaCapturedPitchFrame(
    double? Hertz,
    ushort ClarityPermille,
    short? RmsDecibelsTenths,
    long ArrivalTimestamp,
    long WindowDurationMicroseconds)
{
    public static UtaCapturedPitchFrame FromAnalysis(
        double? hertz,
        double clarity,
        double rms,
        long arrivalTimestamp,
        double windowDurationMilliseconds)
    {
        if (!double.IsFinite(clarity))
            throw new ArgumentOutOfRangeException(nameof(clarity));
        if (!double.IsFinite(rms) || rms < 0)
            throw new ArgumentOutOfRangeException(nameof(rms));
        if (arrivalTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(arrivalTimestamp));
        if (!double.IsFinite(windowDurationMilliseconds) || windowDurationMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(windowDurationMilliseconds));

        double? usableHertz = hertz is { } value && UtaPitchMath.IsFinitePitch(value) ? value : null;
        ushort clarityPermille = checked((ushort)Math.Round(
            Math.Clamp(clarity, 0, 1) * UtaScoringOptions.QUALITY_SCALE,
            MidpointRounding.AwayFromZero));
        short? rmsDecibelsTenths = rms > 0
            ? checked((short)Math.Clamp(
                Math.Round(200 * Math.Log10(rms), MidpointRounding.AwayFromZero),
                -1200,
                120))
            : null;
        long windowMicroseconds = checked((long)Math.Round(
            windowDurationMilliseconds * 1000,
            MidpointRounding.AwayFromZero));

        return new UtaCapturedPitchFrame(
            usableHertz,
            clarityPermille,
            rmsDecibelsTenths,
            arrivalTimestamp,
            windowMicroseconds);
    }

    public UtaScoringFrame MapToScoringFrame(UtaGameplayTimelineMapper mapper, long microphoneLatencyMicroseconds)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        Validate();

        UtaMappedGameplayTime mapped = mapper.MapCaptureCentre(
            ArrivalTimestamp,
            WindowDurationMicroseconds,
            microphoneLatencyMicroseconds);
        bool voiced = Hertz is { } value && UtaPitchMath.IsFinitePitch(value);
        int pitchCents = voiced
            ? checked((int)Math.Round(
                UtaPitchMath.FrequencyToMidi(Hertz!.Value) * 100,
                MidpointRounding.AwayFromZero))
            : 0;

        return new UtaScoringFrame(
            mapped.SongTimeMicroseconds,
            pitchCents,
            ClarityPermille,
            voiced,
            mapped.TimelineEpoch);
    }

    public void Validate()
    {
        if (ClarityPermille > UtaScoringOptions.QUALITY_SCALE)
            throw new ArgumentOutOfRangeException(nameof(ClarityPermille));
        if (RmsDecibelsTenths is < -1200 or > 120)
            throw new ArgumentOutOfRangeException(nameof(RmsDecibelsTenths));
        if (ArrivalTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(ArrivalTimestamp));
        if (WindowDurationMicroseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(WindowDurationMicroseconds));
        if (Hertz is { } hertz && !UtaPitchMath.IsFinitePitch(hertz))
            throw new ArgumentOutOfRangeException(nameof(Hertz));
    }
}
