// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Rulesets.Uta.Skinning.Lookups;

namespace osu.Game.Rulesets.Uta.Skinning;

public static class UtaSkinAssetNames
{
    public const string Marker = "uta-skin-marker";
    public const string PitchPanel = "uta-pitch-panel";
    public const string HudPanel = "uta-hud-panel";
    public const string HudAccent = "uta-hud-accent";
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
    public const string ParticleSing = "uta-particle-sing";
    public const string ParticleScore = "uta-particle-score";

    private static readonly HashSet<string> known = new(StringComparer.Ordinal)
    {
        Marker,
        PitchPanel,
        HudPanel,
        HudAccent,
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
        "uta-target-note-golden-freestyle",
        "uta-target-note-golden-rap",
        "uta-target-note-golden-spoken",
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
        ParticleSing,
        ParticleScore,
    };

    public static bool IsKnown(string componentName) => known.Contains(componentName);

    public static string Feedback(UtaNoteGrade grade) => grade switch
    {
        UtaNoteGrade.Perfect => "uta-feedback-perfect",
        UtaNoteGrade.Great => "uta-feedback-great",
        UtaNoteGrade.Good => "uta-feedback-good",
        UtaNoteGrade.Bad => "uta-feedback-bad",
        _ => "uta-feedback-miss",
    };

    public static string Fault(UtaPitchFault fault) => fault switch
    {
        UtaPitchFault.High => "uta-fault-high",
        UtaPitchFault.Low => "uta-fault-low",
        UtaPitchFault.Unstable => "uta-fault-unstable",
        UtaPitchFault.LowCoverage => "uta-fault-coverage",
        _ => "uta-fault-inaccurate",
    };

    public static string TargetNote(UtaTargetNoteKind kind) => kind switch
    {
        UtaTargetNoteKind.Golden => "uta-target-note-golden",
        UtaTargetNoteKind.Freestyle => "uta-target-note-freestyle",
        UtaTargetNoteKind.Rap => "uta-target-note-rap",
        UtaTargetNoteKind.Spoken => "uta-target-note-spoken",
        UtaTargetNoteKind.GoldenFreestyle => "uta-target-note-golden-freestyle",
        UtaTargetNoteKind.GoldenRap => "uta-target-note-golden-rap",
        UtaTargetNoteKind.GoldenSpoken => "uta-target-note-golden-spoken",
        _ => "uta-target-note-normal",
    };
}
