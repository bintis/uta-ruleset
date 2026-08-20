// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Rulesets.Uta.Skinning;
using osu.Game.Rulesets.Uta.UI.HUD.Pitch;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

internal partial class UtaPitchGuideRenderer : CompositeDrawable
{
    internal const double LOOK_BEHIND = UtaPitchTimelineGeometry.LOOK_BEHIND;
    internal const double LOOK_AHEAD = UtaPitchTimelineGeometry.LOOK_AHEAD;
    internal const float VIEW_SPAN = UtaPitchTimelineGeometry.VIEW_SPAN;
    internal const float PLAYHEAD_POSITION = UtaPitchTimelineGeometry.PLAYHEAD_POSITION;
    private const float base_low_midi = 48;
    private const float base_high_midi = 67;
    private const float edge_margin = 1.5f;
    private const float axis_width = 50;

    private static readonly Color4 panel_background = new(13, 15, 26, 255);
    private static readonly Color4 target_pitch = new(126, 124, 181, 255);

    private readonly Container noteLayer;
    private readonly Container gridLayer;
    private readonly Container axisLayer;
    private readonly Box panel;
    private readonly Sprite panelTexture;
    private readonly UtaTexturedPrimitive playhead;
    private readonly UtaPitchCurveGraph curveGraph;
    private readonly UtaPitchGuideTrail trail;
    private readonly List<TargetNote> targetNotes = new();
    private TargetNote[] commitOrder = Array.Empty<TargetNote>();
    private readonly List<UtaTexturedPrimitive> gridLines = new((int)VIEW_SPAN + 1);
    private readonly Dictionary<int, OsuSpriteText> pitchLabels = new(5);
    private readonly BindableFloat keyShiftSemitones = new();

    private UtaGameplayScoringController? scoringController;
    private UtaNote[] notes = Array.Empty<UtaNote>();
    private readonly BindableFloat centreMidi = new((base_low_midi + base_high_midi) / 2);
    private double maximumNoteDuration;
    private int previousRangeStart;
    private int previousRangeEnd;
    private int nextCommitIndex;
    private float lastLayoutCentre = float.NaN;
    private UtaVisualStyle style = UtaVisualStyle.Prism();
    private UtaVisualStyleProvider? styleProvider;

    public UtaPitchGuideRenderer()
    {
        RelativeSizeAxes = Axes.Both;
        Masking = true;

        InternalChildren = new Drawable[]
        {
            panel = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = panel_background,
            },
            panelTexture = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Stretch,
                Alpha = 0,
            },
            gridLayer = new Container { RelativeSizeAxes = Axes.Both },
            curveGraph = new UtaPitchCurveGraph(),
            noteLayer = new Container { RelativeSizeAxes = Axes.Both },
            trail = new UtaPitchGuideTrail(),
            playhead = new UtaTexturedPrimitive
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopCentre,
                RelativePositionAxes = Axes.X,
                X = PLAYHEAD_POSITION,
                RelativeSizeAxes = Axes.Y,
                Width = 2,
                Colour = new Color4(172, 164, 218, 255),
                Alpha = 0.56f,
            },
            axisLayer = new Container
            {
                RelativeSizeAxes = Axes.Y,
                Width = axis_width,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(8, 10, 20, 255),
                        Alpha = 0.46f,
                    },
                    new Box
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        RelativeSizeAxes = Axes.Y,
                        Width = 1,
                        Colour = new Color4(174, 177, 210, 255),
                        Alpha = 0.12f,
                    },
                },
            },
        };

        for (int i = 0; i <= (int)VIEW_SPAN; i++)
        {
            var line = new UtaTexturedPrimitive
            {
                RelativePositionAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
                Height = i % 12 == 0 ? 1.2f : 0.65f,
                Colour = new Color4(166, 169, 202, 255),
                Alpha = i % 12 == 0 ? 0.16f : 0.06f,
            };
            gridLines.Add(line);
            gridLayer.Add(line);
        }

        foreach (int midi in new[] { 48, 53, 60, 67, 72 })
        {
            var label = new OsuSpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.CentreRight,
                RelativePositionAxes = Axes.Y,
                X = axis_width - 7,
                Text = midiName(midi),
                Font = OsuFont.Default.With(size: 9, weight: FontWeight.SemiBold),
                Colour = new Color4(202, 204, 225, 255),
                Alpha = 0,
            };
            pitchLabels.Add(midi, label);
            axisLayer.Add(label);
        }
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap, UtaRulesetConfigManager config,
                      UtaPitchViewport pitchViewport, UtaGameplayScoringController scoringController, UtaVisualStyleProvider styleProvider)
    {
        this.styleProvider = styleProvider;
        styleProvider.StyleChanged += applyStyle;
        applyStyle(styleProvider.Style);
        this.scoringController = scoringController;
        if (string.IsNullOrEmpty(beatmap.PackageId))
        {
            Hide();
            return;
        }

        notes = beatmap.HitObjects.OfType<UtaNote>()
                       .Where(note => note.Midi != null)
                       .OrderBy(note => note.StartTime)
                       .ToArray();
        centreMidi.BindTo(pitchViewport.CentreMidi);
        keyShiftSemitones.BindTo(config.GetBindable<float>(UtaRulesetSetting.KeyShiftSemitones));
        maximumNoteDuration = notes.Length == 0 ? 0 : notes.Max(note => note.Duration);

        targetNotes.Capacity = notes.Length;
        foreach (var note in notes)
        {
            var drawable = new TargetNote(note);
            drawable.ApplyStyle(style);
            targetNotes.Add(drawable);
            noteLayer.Add(drawable);
        }

        commitOrder = targetNotes.OrderBy(target => target.Note.EndTime).ToArray();
        updateStaticPitchLayout();
    }

    private void applyStyle(UtaVisualStyle value)
    {
        style = value;
        panel.Colour = value.Pitch.Panel;
        panel.Alpha = value.Pitch.Opacity;
        panelTexture.Texture = value.Assets.PitchPanel;
        panelTexture.Alpha = value.Assets.PitchPanel == null ? 0 : value.Pitch.Opacity;
        playhead.Colour = value.Pitch.Playhead;
        playhead.Width = Math.Max(2, value.Pitch.LiveCurveWeight * 0.65f);
        playhead.Texture = value.Assets.Playhead;
        curveGraph.ApplyStyle(value);
        trail.ApplyStyle(value);

        foreach (TargetNote target in targetNotes)
            target.ApplyStyle(value);
        updateStaticPitchLayout(true);
    }

    protected override void Update()
    {
        base.Update();

        if (notes.Length == 0 || DrawHeight <= 0)
            return;

        double current = Time.Current;

        updateStaticPitchLayout();

        float shiftedCentre = centreMidi.Value + keyShiftSemitones.Value;
        float low = shiftedCentre - VIEW_SPAN / 2;
        float high = shiftedCentre + VIEW_SPAN / 2;

        int rangeStart = lowerBoundStart(current - LOOK_BEHIND - maximumNoteDuration);
        int rangeEnd = upperBoundStart(current + LOOK_AHEAD);

        for (int i = previousRangeStart; i < previousRangeEnd; i++)
        {
            if (i < rangeStart || i >= rangeEnd)
                targetNotes[i].Alpha = 0;
        }

        for (int i = rangeStart; i < rangeEnd; i++)
        {
            TargetNote target = targetNotes[i];
            UtaNote note = target.Note;
            UtaPitchTargetGeometry geometry = UtaPitchTimelineGeometry.Target(
                note.StartTime,
                note.EndTime,
                note.Midi!.Value,
                current,
                shiftedCentre,
                DrawWidth);
            target.Alpha = geometry.Visible ? 1 : 0;
            if (!geometry.Visible)
                continue;

            target.X = geometry.X;
            target.Width = geometry.Width;
            target.Y = geometry.Y;
        }
        previousRangeStart = rangeStart;
        previousRangeEnd = rangeEnd;

        while (nextCommitIndex < commitOrder.Length)
        {
            TargetNote pending = commitOrder[nextCommitIndex];
            if (current < pending.Note.EndTime)
                break;

            if (scoringController is { ScoringEnabled: true })
            {
                if (!scoringController.TryPreviewCompletedNote(pending.Note.ScoringIndex, out UtaNoteScore? score) || score == null)
                    break;

                pending.CommitColour(score.Grade);
            }

            nextCommitIndex++;
        }
    }

    private void updateStaticPitchLayout(bool force = false)
    {
        float shiftedCentre = centreMidi.Value + keyShiftSemitones.Value;
        if (!force && shiftedCentre.Equals(lastLayoutCentre))
            return;

        lastLayoutCentre = shiftedCentre;
        float high = shiftedCentre + VIEW_SPAN / 2;
        int firstGridMidi = (int)MathF.Floor(shiftedCentre - VIEW_SPAN / 2);

        for (int i = 0; i < gridLines.Count; i++)
        {
            int midi = firstGridMidi + i;
            int pitchClass = ((midi % 12) + 12) % 12;
            bool labelled = isLabelledMidi(midi);
            gridLines[i].Y = (high - midi) / VIEW_SPAN;
            gridLines[i].Colour = labelled ? style.Pitch.GridMajor : style.Pitch.GridMinor;
            gridLines[i].Height = labelled ? style.Pitch.GridMajorWeight : style.Pitch.GridMinorWeight;
            gridLines[i].Alpha = labelled ? 0.16f : pitchClass is 1 or 3 or 6 or 8 or 10 ? 0.032f : 0.055f;
            gridLines[i].Texture = labelled ? style.Assets.GridMajor : style.Assets.GridMinor;
        }

        foreach (var (midi, label) in pitchLabels)
        {
            float y = (high - midi) / VIEW_SPAN;
            label.Y = y;
            label.Colour = style.Pitch.Axis;
            label.Alpha = y is >= 0.035f and <= 0.965f ? 0.66f : 0;
        }
    }

    private static bool isLabelledMidi(int midi) => midi is 48 or 53 or 60 or 67 or 72;

    private int lowerBoundStart(double time)
    {
        int low = 0;
        int high = notes.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (notes[middle].StartTime < time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private int upperBoundStart(double time)
    {
        int low = 0;
        int high = notes.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (notes[middle].StartTime <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static string midiName(int midi)
    {
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return $"{names[((midi % 12) + 12) % 12]}{midi / 12 - 1}";
    }

    internal static float CalculateFixedCentre(IReadOnlyList<UtaNote> songNotes)
    {
        float targetCentre = (base_low_midi + base_high_midi) / 2;
        UtaNote[] pitched = songNotes.Where(note => note.Midi != null).ToArray();

        if (pitched.Length > 0)
        {
            float low = pitched.Min(note => (float)note.Midi!.Value);
            float high = pitched.Max(note => (float)note.Midi!.Value);

            if (high - low > VIEW_SPAN - edge_margin * 2)
                targetCentre = (low + high) / 2;
            else if (low < base_low_midi)
                targetCentre = low - edge_margin + VIEW_SPAN / 2;
            else if (high > base_high_midi)
                targetCentre = high + edge_margin - VIEW_SPAN / 2;
        }

        return MathF.Round(Math.Clamp(targetCentre, 40 + VIEW_SPAN / 2, 88 - VIEW_SPAN / 2) * 2) / 2;
    }

    protected override void Dispose(bool isDisposing)
    {
        keyShiftSemitones.UnbindAll();
        centreMidi.UnbindAll();
        if (styleProvider != null)
            styleProvider.StyleChanged -= applyStyle;
        base.Dispose(isDisposing);
    }

    private partial class TargetNote : CircularContainer
    {
        public UtaNote Note { get; }
        public bool ColourCommitted { get; private set; }

        private readonly Box fill;
        private readonly Box criticalCue;
        private readonly Box outlineTop;
        private readonly Box outlineBottom;
        private readonly Box outlineLeft;
        private readonly Box outlineRight;
        private readonly Sprite texture;
        private UtaVisualStyle style = UtaVisualStyle.Prism();

        public TargetNote(UtaNote note)
        {
            Note = note;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.CentreLeft;
            RelativePositionAxes = Axes.Both;
            RelativeSizeAxes = Axes.X;
            Height = style.Pitch.TargetNoteHeight;
            Alpha = 0;
            Masking = true;
            BorderThickness = 1.35f;
            BorderColour = new Color4(target_pitch.R, target_pitch.G, target_pitch.B, 0.58f);
            Children = new Drawable[]
            {
                fill = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = target_pitch,
                    Alpha = note.NoteKind == "freestyle" || note.NoteKind == "spoken" ? 0.10f : 0.16f,
                },
                texture = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    FillMode = FillMode.Stretch,
                    Alpha = 0,
                },
                outlineTop = new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 2,
                    Colour = target_pitch,
                },
                outlineBottom = new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 2,
                    Colour = target_pitch,
                },
                outlineLeft = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 2,
                    Colour = target_pitch,
                },
                outlineRight = new Box
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    RelativeSizeAxes = Axes.Y,
                    Width = 2,
                    Colour = target_pitch,
                },
                criticalCue = new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.X,
                    Width = 1,
                    Height = 1.2f,
                    Colour = target_pitch,
                    Alpha = 0.28f,
                },
            };
        }

        public void ApplyStyle(UtaVisualStyle value)
        {
            style = value;
            Height = value.Pitch.TargetNoteHeight;
            CornerRadius = value.Pitch.TargetNoteCornerRadius;
            Color4 accent = colourForKind(Note.NoteKind, value.Pitch);
            float outlineThickness = Math.Clamp(value.Pitch.TargetNoteBorder, 1.15f, 1.8f);
            Color4 outlineColour = new Color4(accent.R, accent.G, accent.B, 0.56f);
            BorderThickness = outlineThickness;
            BorderColour = outlineColour;
            outlineTop.Height = outlineBottom.Height = 1;
            outlineLeft.Width = outlineRight.Width = 1;
            outlineTop.Colour = outlineBottom.Colour = outlineLeft.Colour = outlineRight.Colour = outlineColour;
            outlineTop.Alpha = outlineBottom.Alpha = outlineLeft.Alpha = outlineRight.Alpha = 0;
            if (!ColourCommitted)
            {
                bool airy = Note.NoteKind is "freestyle" or "spoken";
                fill.Colour = accent;
                fill.Alpha = airy ? 0.09f : 0.16f;
                criticalCue.Colour = new Color4(accent.R, accent.G, accent.B, 0.72f);
                criticalCue.Alpha = 0.22f;
            }
            texture.Texture = value.Assets.TargetFor(Note.NoteKind);
            texture.Colour = Color4.White;
            texture.Alpha = texture.Texture == null ? 0 : 1;
            criticalCue.Height = 1.15f;
        }

        private static Color4 colourForKind(string? kind, UtaPitchStyle pitch)
        {
            string normalised = (kind ?? string.Empty).Replace('-', '_').ToLowerInvariant();
            if (normalised.Contains("golden", StringComparison.Ordinal))
                return pitch.TargetGolden;
            if (normalised.Contains("freestyle", StringComparison.Ordinal))
                return pitch.TargetFreestyle;
            if (normalised.Contains("rap", StringComparison.Ordinal))
                return pitch.TargetRap;
            if (normalised.Contains("spoken", StringComparison.Ordinal))
                return pitch.TargetSpoken;
            return pitch.Target;
        }

        public void CommitColour(UtaNoteGrade grade)
        {
            if (ColourCommitted)
                return;

            ColourCommitted = true;
            (Color4 colour, float alpha, float glow) = styleFor(grade);

            BorderColour = colour;
            fill.FadeColour(colour, 180, Easing.OutQuint);
            fill.FadeTo(alpha, 180, Easing.OutQuint);
            criticalCue.FadeColour(colour, 180, Easing.OutQuint);
            criticalCue.FadeTo(0.95f, 180, Easing.OutQuint);
            texture.Colour = colour;
            texture.FadeTo(texture.Texture == null ? 0 : 1, 180, Easing.OutQuint);
            outlineTop.FadeColour(colour, 180, Easing.OutQuint);
            outlineBottom.FadeColour(colour, 180, Easing.OutQuint);
            outlineLeft.FadeColour(colour, 180, Easing.OutQuint);
            outlineRight.FadeColour(colour, 180, Easing.OutQuint);
            EdgeEffect = glow > 0
                ? new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = new Color4(colour.R, colour.G, colour.B, grade == UtaNoteGrade.Perfect ? 0.45f : 0.24f),
                    Radius = glow,
                }
                : default;
        }

        private (Color4 Colour, float Alpha, float Glow) styleFor(UtaNoteGrade grade)
            => grade switch
            {
                UtaNoteGrade.Perfect => (style.Feedback.Perfect, 0.88f, 8),
                UtaNoteGrade.Great => (style.Feedback.Great, 0.82f, 5),
                UtaNoteGrade.Good => (style.Feedback.Good, 0.76f, 3),
                UtaNoteGrade.Bad => (style.Feedback.Bad, 0.68f, 0),
                _ => (style.Feedback.Miss, 0.58f, 0),
            };

    }
}
