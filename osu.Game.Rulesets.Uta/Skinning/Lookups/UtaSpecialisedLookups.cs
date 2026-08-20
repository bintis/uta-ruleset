// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Uta.Skinning.Lookups;

public enum UtaTargetNoteKind
{
    Normal,
    Golden,
    Freestyle,
    Rap,
    Spoken,
    GoldenFreestyle,
    GoldenRap,
    GoldenSpoken,
}

public enum UtaTargetNoteState
{
    Upcoming,
    Active,
    Completed,
}

public readonly record struct UtaTargetNoteLookup(UtaTargetNoteKind Kind, UtaTargetNoteState State) : ISkinComponentLookup;

public enum UtaCurveRole
{
    Reference,
    Live,
    Trail,
}

public readonly record struct UtaCurveLookup(UtaCurveRole Role) : ISkinComponentLookup;

public enum UtaGridRole
{
    Major,
    Minor,
    Octave,
}

public readonly record struct UtaGridLookup(UtaGridRole Role) : ISkinComponentLookup;

public enum UtaLyricsDecorationRole
{
    Panel,
    CurrentUnderline,
    ReadingMarker,
    ProgressFill,
    UpcomingMarker,
}

public readonly record struct UtaLyricsDecorationLookup(UtaLyricsDecorationRole Role) : ISkinComponentLookup;

[Flags]
public enum UtaFeedbackFaults
{
    None = 0,
    High = 1 << 0,
    Low = 1 << 1,
    Unstable = 1 << 2,
    Coverage = 1 << 3,
    Inaccurate = 1 << 4,
}

public readonly record struct UtaScoringFeedbackLookup(UtaNoteGrade Grade, UtaFeedbackFaults Faults) : ISkinComponentLookup;

public enum UtaParticleRole
{
    Singing,
    Scoring,
}

public readonly record struct UtaParticleLookup(UtaParticleRole Role) : ISkinComponentLookup;
