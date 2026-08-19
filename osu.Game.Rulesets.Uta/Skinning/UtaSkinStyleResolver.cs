// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Uta.UI.HUD;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.Skinning;

public static class UtaSkinStyleResolver
{
    public static UtaVisualStyle Resolve(ISkinSource source, UtaHudDensity density, bool reducedMotion, float pitchOpacity = 1, float lyricsPanelOpacity = 0.72f)
    {
        UtaVisualStyle prism = CreatePrism(density, reducedMotion);
        Color4 panel = readColour(source, UtaSkinConfiguration.SurfaceColour, prism.Pitch.Panel, UtaAccessiblePalette.Background, 1.2);
        var pitch = prism.Pitch with
        {
            Panel = panel,
            GridMajor = readColour(source, UtaSkinConfiguration.GridColour, prism.Pitch.GridMajor, panel, 3),
            Target = readColour(source, UtaSkinConfiguration.TargetColour, prism.Pitch.Target, panel, 3),
            Reference = readColour(source, UtaSkinConfiguration.SongCurveColour, prism.Pitch.Reference, panel, 3),
            LiveAccurate = readColour(source, UtaSkinConfiguration.LiveCurveColour, prism.Pitch.LiveAccurate, panel, 3),
            Playhead = readColour(source, UtaSkinConfiguration.PlayheadColour, prism.Pitch.Playhead, panel, 3),
            GridMajorWeight = readFloat(source, UtaSkinConfiguration.GridMajorWeight, prism.Pitch.GridMajorWeight, 0.5f, 4),
            GridMinorWeight = readFloat(source, UtaSkinConfiguration.GridMinorWeight, prism.Pitch.GridMinorWeight, 0.25f, 3),
            ReferenceCurveWeight = readFloat(source, UtaSkinConfiguration.ReferenceCurveWeight, prism.Pitch.ReferenceCurveWeight, 1, 8),
            LiveCurveWeight = readFloat(source, UtaSkinConfiguration.LiveCurveWeight, prism.Pitch.LiveCurveWeight, 1.5f, 10),
            TargetNoteHeight = readFloat(source, UtaSkinConfiguration.TargetNoteHeight, prism.Pitch.TargetNoteHeight, 6, 24),
            TargetNoteBorder = readFloat(source, UtaSkinConfiguration.TargetNoteBorder, prism.Pitch.TargetNoteBorder, 0, 5),
            Opacity = Math.Clamp(pitchOpacity, 0.5f, 1),
        };
        var lyrics = prism.Lyrics with
        {
            Panel = panel,
            Current = readColour(source, UtaSkinConfiguration.LyricsCurrentColour, prism.Lyrics.Current, panel, 4.5),
            Sung = readColour(source, UtaSkinConfiguration.LyricsSungColour, prism.Lyrics.Sung, panel, 4.5),
            Reading = readColour(source, UtaSkinConfiguration.LyricsReadingColour, prism.Lyrics.Reading, panel, 4.5),
            Upcoming = readColour(source, UtaSkinConfiguration.LyricsUpcomingColour, prism.Lyrics.Upcoming, panel, 4.5),
            CurrentSize = readFloat(source, UtaSkinConfiguration.LyricsCurrentSize, prism.Lyrics.CurrentSize, UtaHudLayoutCoordinator.MINIMUM_LYRICS_FONT_SIZE, 64),
            ReadingSize = readFloat(source, UtaSkinConfiguration.LyricsReadingSize, prism.Lyrics.ReadingSize, 9, 24),
            UpcomingSize = readFloat(source, UtaSkinConfiguration.LyricsUpcomingSize, prism.Lyrics.UpcomingSize, 14, 36),
            PanelOpacity = Math.Clamp(lyricsPanelOpacity, 0, 0.95f),
        };

        return prism with { Pitch = pitch, Lyrics = lyrics, Assets = resolveAssets(source) };
    }

    internal static UtaVisualStyle CreatePrism(UtaHudDensity density, bool reducedMotion)
    {
        Color4 panel = new(11, 16, 32, 255);
        var pitch = new UtaPitchStyle(
            panel,
            new Color4(126, 138, 168, 255),
            new Color4(76, 88, 116, 255),
            new Color4(202, 204, 225, 255),
            new Color4(130, 199, 255, 255),
            new Color4(214, 186, 126, 255),
            new Color4(186, 164, 228, 255),
            new Color4(214, 168, 122, 255),
            new Color4(185, 194, 217, 255),
            new Color4(110, 182, 255, 255),
            new Color4(245, 247, 255, 255),
            new Color4(57, 217, 198, 255),
            new Color4(242, 201, 76, 255),
            new Color4(255, 138, 91, 255),
            new Color4(184, 153, 255, 255),
            1.25f,
            0.75f,
            2.25f,
            3.25f,
            4.25f,
            10,
            11,
            2,
            5.5f,
            1);
        float currentSize = density == UtaHudDensity.Narrow ? 25 : 31;
        var lyrics = new UtaLyricsStyle(
            panel,
            new Color4(244, 247, 255, 255),
            new Color4(150, 224, 255, 255),
            new Color4(212, 200, 245, 255),
            new Color4(212, 200, 245, 255),
            new Color4(185, 194, 217, 255),
            new Color4(207, 191, 255, 255),
            Color4.Black,
            currentSize,
            density == UtaHudDensity.Narrow ? 10 : 11.5f,
            density == UtaHudDensity.Narrow ? 16 : 18,
            0.72f,
            2);
        var feedback = new UtaFeedbackStyle(
            new Color4(246, 211, 101, 255),
            new Color4(86, 180, 233, 255),
            new Color4(57, 217, 138, 255),
            new Color4(230, 159, 0, 255),
            new Color4(242, 107, 138, 255));
        var motion = reducedMotion
            ? new UtaMotionStyle(0, 0, 0, 80, 0, 0, true)
            : new UtaMotionStyle(0.65f, 220, 220, 180, 18, 24, false);
        return new UtaVisualStyle(pitch, lyrics, feedback, motion, new UtaSkinAssets(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null), density);
    }

    private static Color4 readColour(ISkin source, UtaSkinConfiguration role, Color4 fallback, Color4 background, double contrast)
    {
        IBindable<Color4>? configured = source.GetConfig<UtaSkinConfigurationLookup, Color4>(new UtaSkinConfigurationLookup(role));
        Color4 value = configured?.Value ?? fallback;
        if (value.A < 0.08f)
            value = fallback;
        return UtaAccessiblePalette.EnsureContrast(value, background, contrast);
    }

    private static float readFloat(ISkin source, UtaSkinConfiguration role, float fallback, float minimum, float maximum)
    {
        IBindable<float>? configured = source.GetConfig<UtaSkinConfigurationLookup, float>(new UtaSkinConfigurationLookup(role));
        float value = configured?.Value ?? fallback;
        return float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }

    private static UtaSkinAssets resolveAssets(ISkinSource source)
    {
        ISkin? provider = source.FindProvider(s => s.GetTexture(UtaSkinAssetNames.Marker) != null);
        if (provider == null)
            return CreatePrism(UtaHudDensity.Standard, false).Assets;

        Texture? get(string name) => provider.GetTexture(name);
        return new UtaSkinAssets(
            get(UtaSkinAssetNames.PitchPanel),
            get("uta-target-note-normal"),
            get("uta-target-note-golden"),
            get("uta-target-note-freestyle"),
            get("uta-target-note-rap"),
            get("uta-target-note-spoken"),
            get(UtaSkinAssetNames.Playhead),
            get(UtaSkinAssetNames.GridMajor),
            get(UtaSkinAssetNames.GridMinor),
            get(UtaSkinAssetNames.CurveReference),
            get(UtaSkinAssetNames.CurveLive),
            get(UtaSkinAssetNames.CurveTrail),
            get(UtaSkinAssetNames.LyricsPanel),
            get(UtaSkinAssetNames.LyricsUnderline),
            get(UtaSkinAssetNames.LyricsReadingMarker),
            get(UtaSkinAssetNames.LyricsProgress),
            get(UtaSkinAssetNames.LyricsUpcomingMarker));
    }
}
