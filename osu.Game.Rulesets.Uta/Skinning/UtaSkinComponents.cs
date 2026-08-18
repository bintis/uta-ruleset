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
    GridColour,
    TargetColour,
    SongCurveColour,
    LiveCurveColour,
    PlayheadColour,
    GoodFeedbackColour,
    BadFeedbackColour,
    LineWeight,
    NoteSpacing,
    AnimationIntensity,
}
