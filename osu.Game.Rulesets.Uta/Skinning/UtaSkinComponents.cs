// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Uta.Skinning;

public sealed class UtaSkinComponentLookup : SkinComponentLookup<UtaSkinComponents>
{
    public UtaSkinComponentLookup(UtaSkinComponents component)
        : base(component)
    {
    }
}

public enum UtaSkinComponents
{
    Grid,
    TargetNote,
    SongPitchCurve,
    LivePitchCurve,
    Playhead,
    LyricsPanel,
    ScoringFeedback,
    SingingParticle,
    ScoringParticle,
}

public sealed class UtaSkinConfigurationLookup
{
    public UtaSkinConfiguration Lookup { get; }

    public UtaSkinConfigurationLookup(UtaSkinConfiguration lookup)
    {
        Lookup = lookup;
    }
}

public enum UtaSkinConfiguration
{
    GridColour = 0,
    TargetColour = 1,
    SongCurveColour = 2,
    LiveCurveColour = 3,
    PlayheadColour = 4,
    GoodFeedbackColour = 5,
    BadFeedbackColour = 6,
    LineWeight = 7,
    NoteSpacing = 8,
    AnimationIntensity = 9,
    SurfaceColour = 10,
    GridMajorWeight = 11,
    GridMinorWeight = 12,
    ReferenceCurveWeight = 13,
    LiveCurveWeight = 14,
    TargetNoteHeight = 15,
    TargetNoteBorder = 16,
    LyricsCurrentColour = 17,
    LyricsSungColour = 18,
    LyricsReadingColour = 19,
    LyricsUpcomingColour = 20,
    LyricsCurrentSize = 21,
    LyricsReadingSize = 22,
    LyricsUpcomingSize = 23,
}
