// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Core;

public static class UtaScoringBeatmapAdapter
{
    public static IReadOnlyList<UtaScoringTarget> CreateTargets(UtaBeatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);

        return beatmap.HitObjects.OfType<UtaNote>()
                      .OrderBy(note => note.StartTime)
                      .ThenBy(note => note.ScoringIndex)
                      .Select((note, fallbackIndex) => CreateTarget(note, fallbackIndex))
                      .ToArray();
    }

    public static bool IsScorable(UtaNote note, UtaScoringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(note);
        var actualOptions = options ?? new UtaScoringOptions();
        actualOptions.Validate();
        return UtaScoringMath.IsScorable(CreateTarget(note), actualOptions);
    }

    public static UtaScoringTarget CreateTarget(UtaNote note, int fallbackIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(note);

        int index = note.ScoringIndex >= 0 ? note.ScoringIndex : fallbackIndex;
        long start = checked((long)Math.Round(note.StartTime * 1000, MidpointRounding.AwayFromZero));
        long end = checked((long)Math.Round(note.EndTime * 1000, MidpointRounding.AwayFromZero));
        return UtaScoringTarget.FromConfidence(index, start, end, note.Midi, note.TargetConfidence, parseKind(note.NoteKind));
    }

    private static UtaScoringNoteKind parseKind(string value)
        => value switch
        {
            "golden" => UtaScoringNoteKind.Golden,
            "freestyle" => UtaScoringNoteKind.Freestyle,
            "golden_freestyle" => UtaScoringNoteKind.GoldenFreestyle,
            "rap" => UtaScoringNoteKind.Rap,
            "golden_rap" => UtaScoringNoteKind.GoldenRap,
            "spoken" => UtaScoringNoteKind.Spoken,
            "golden_spoken" => UtaScoringNoteKind.GoldenSpoken,
            _ => UtaScoringNoteKind.Normal,
        };
}
