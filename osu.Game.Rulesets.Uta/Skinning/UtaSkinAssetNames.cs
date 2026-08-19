// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Uta.Skinning.Lookups;

namespace osu.Game.Rulesets.Uta.Skinning;

public static class UtaSkinAssetNames
{
    public const string Marker = "uta-skin-marker";
    public const string PitchPanel = "uta-pitch-panel";
    public const string Playhead = "uta-playhead";
    public const string GridMajor = "uta-grid-major";
    public const string GridMinor = "uta-grid-minor";
    public const string CurveReference = "uta-curve-reference";
    public const string CurveLive = "uta-curve-live";
    public const string CurveTrail = "uta-curve-trail";
    public const string LyricsPanel = "uta-lyrics-panel";
    public const string LyricsUnderline = "uta-lyrics-current-underline";
    public const string LyricsReadingMarker = "uta-lyrics-reading-marker";
    public const string LyricsProgress = "uta-lyrics-progress-fill";
    public const string LyricsUpcomingMarker = "uta-lyrics-upcoming-marker";

    private static readonly HashSet<string> known = new(StringComparer.Ordinal)
    {
        Marker,
        PitchPanel,
        Playhead,
        GridMajor,
        GridMinor,
        CurveReference,
        CurveLive,
        CurveTrail,
        LyricsPanel,
        LyricsUnderline,
        LyricsReadingMarker,
        LyricsProgress,
        LyricsUpcomingMarker,
        "uta-target-note-normal",
        "uta-target-note-golden",
        "uta-target-note-freestyle",
        "uta-target-note-rap",
        "uta-target-note-spoken",
        "uta-feedback-perfect",
        "uta-feedback-great",
        "uta-feedback-good",
        "uta-feedback-bad",
        "uta-feedback-miss",
        "uta-fault-high",
        "uta-fault-low",
        "uta-fault-unstable",
        "uta-fault-coverage",
        "uta-fault-inaccurate",
        "uta-particle-sing",
        "uta-particle-score",
    };

    public static bool IsKnown(string componentName) => known.Contains(componentName);

    public static string TargetNote(UtaTargetNoteKind kind) => kind switch
    {
        UtaTargetNoteKind.Golden => "uta-target-note-golden",
        UtaTargetNoteKind.Freestyle => "uta-target-note-freestyle",
        UtaTargetNoteKind.Rap => "uta-target-note-rap",
        UtaTargetNoteKind.Spoken => "uta-target-note-spoken",
        _ => "uta-target-note-normal",
    };
}
