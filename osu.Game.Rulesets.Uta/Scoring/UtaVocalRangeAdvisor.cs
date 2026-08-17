// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// Tracks a user's confidently detected singing range and recommends a transpose
/// which places the song's robust target range inside the observed range.
/// </summary>
public sealed class UtaVocalRangeAdvisor
{
    private readonly object sync = new();

    // Observations arrive at the pitch-analysis cadence during gameplay. A modest initial
    // capacity avoids the earliest List growth/copy spikes while keeping idle memory small.
    private readonly List<int> observedPitchCents = new(4096);

    public int MinimumObservationCount { get; init; } = 40;

    public void AddObservation(int pitchCents, ushort clarityPermille)
    {
        if (pitchCents is < 0 or > 12_700)
            throw new ArgumentOutOfRangeException(nameof(pitchCents));
        if (clarityPermille > UtaScoringOptions.QUALITY_SCALE)
            throw new ArgumentOutOfRangeException(nameof(clarityPermille));
        if (clarityPermille < 650)
            return;

        lock (sync)
            observedPitchCents.Add(pitchCents);
    }

    public UtaVocalRangeSnapshot Snapshot()
    {
        int[] values;
        lock (sync)
            values = observedPitchCents.ToArray();

        if (values.Length < MinimumObservationCount)
            return new UtaVocalRangeSnapshot(false, 0, 0, values.Length);

        Array.Sort(values);
        return new UtaVocalRangeSnapshot(
            true,
            percentile(values, 0.10),
            percentile(values, 0.90),
            values.Length);
    }

    public UtaTransposeRecommendation Recommend(IEnumerable<UtaScoringTarget> targets, int minimumTranspose = -6, int maximumTranspose = 6)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (minimumTranspose > maximumTranspose)
            throw new ArgumentException("Minimum transpose must not exceed maximum transpose.");

        UtaVocalRangeSnapshot user = Snapshot();
        int[] song = targets
            .Where(t => t.Midi.HasValue && t.Kind is UtaScoringNoteKind.Normal or UtaScoringNoteKind.Golden)
            .Select(t => t.Midi!.Value * 100)
            .OrderBy(v => v)
            .ToArray();

        if (!user.Available || song.Length == 0)
            return new UtaTransposeRecommendation(false, 0, user, default, double.PositiveInfinity);

        int songLow = percentile(song, 0.05);
        int songHigh = percentile(song, 0.95);
        int best = 0;
        double bestPenalty = double.PositiveInfinity;

        for (int semitones = minimumTranspose; semitones <= maximumTranspose; semitones++)
        {
            int shiftedLow = songLow + semitones * 100;
            int shiftedHigh = songHigh + semitones * 100;
            int below = Math.Max(0, user.LowPitchCents - shiftedLow);
            int above = Math.Max(0, shiftedHigh - user.HighPitchCents);

            double userCentre = (user.LowPitchCents + user.HighPitchCents) / 2.0;
            double songCentre = (shiftedLow + shiftedHigh) / 2.0;
            double penalty = below * below + above * above + Math.Abs(songCentre - userCentre) * 0.25;

            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                best = semitones;
            }
        }

        return new UtaTransposeRecommendation(
            true,
            best,
            user,
            new UtaSongRange(songLow, songHigh),
            bestPenalty);
    }

    private static int percentile(int[] sorted, double fraction)
    {
        if (sorted.Length == 0)
            return 0;

        int index = (int)Math.Round((sorted.Length - 1) * fraction, MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}

public readonly record struct UtaVocalRangeSnapshot(bool Available, int LowPitchCents, int HighPitchCents, int ObservationCount);
public readonly record struct UtaSongRange(int LowPitchCents, int HighPitchCents);
public readonly record struct UtaTransposeRecommendation(
    bool Available,
    int Semitones,
    UtaVocalRangeSnapshot UserRange,
    UtaSongRange SongRange,
    double Penalty);
