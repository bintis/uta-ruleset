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
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Uta's flowing pitch guide, using the same 1.75s history / 5.25s look-ahead
/// window and nineteen-semitone adaptive viewport as the standalone player.
/// </summary>
public partial class UtaPitchGuide : CompositeDrawable
{
    internal const double LOOK_BEHIND = 1750;
    internal const double LOOK_AHEAD = 5250;
    internal const float VIEW_SPAN = 19;
    internal const float PLAYHEAD_POSITION = (float)(LOOK_BEHIND / (LOOK_BEHIND + LOOK_AHEAD));

    private const double window = LOOK_BEHIND + LOOK_AHEAD;
    private const float base_low_midi = 48;
    private const float base_high_midi = 67;
    private const float edge_margin = 1.5f;
    private const float axis_width = 50;

    private static readonly Color4 panel_background = new(13, 15, 26, 255);
    private static readonly Color4 target_pitch = new(126, 124, 181, 255);

    private readonly Container noteLayer;
    private readonly Container gridLayer;
    private readonly Container axisLayer;
    private readonly List<TargetNote> targetNotes = new();
    private TargetNote[] commitOrder = Array.Empty<TargetNote>();
    private readonly List<Box> gridLines = new();
    private readonly Dictionary<int, OsuSpriteText> pitchLabels = new();
    private readonly BindableFloat pitchDeviation = new();
    private readonly BindableFloat pitchSimilarity = new();
    private readonly BindableBool voiceActive = new();

    private UtaNote[] notes = Array.Empty<UtaNote>();
    private float centreMidi = (base_low_midi + base_high_midi) / 2;
    private double maximumNoteDuration;
    private double lastUpdateTime = double.NegativeInfinity;
    private int previousRangeStart;
    private int previousRangeEnd;
    private int nextCommitIndex;

    public UtaPitchGuide()
    {
        RelativeSizeAxes = Axes.X;
        Width = 1;
        Height = 168;
        Anchor = Anchor.TopCentre;
        Origin = Anchor.TopCentre;
        Y = 24;
        Masking = true;

        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = panel_background,
            },
            gridLayer = new Container { RelativeSizeAxes = Axes.Both },
            new UtaPitchCurveGraph(),
            noteLayer = new Container { RelativeSizeAxes = Axes.Both },
            new UtaPitchGuideTrail(),
            new Box
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
            var line = new Box
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
    private void load(UtaBeatmap beatmap, osu.Game.Rulesets.UI.DrawableRuleset drawableRuleset)
    {
        if (string.IsNullOrEmpty(beatmap.PackageId))
        {
            Hide();
            return;
        }

        notes = beatmap.HitObjects.OfType<UtaNote>()
                       .Where(note => note.Midi != null)
                       .OrderBy(note => note.StartTime)
                       .ToArray();
        centreMidi = CalculateFixedCentre(notes);
        maximumNoteDuration = notes.Length == 0 ? 0 : notes.Max(note => note.Duration);

        foreach (var note in notes)
        {
            var drawable = new TargetNote(note);
            targetNotes.Add(drawable);
            noteLayer.Add(drawable);
        }

        commitOrder = targetNotes.OrderBy(target => target.Note.EndTime).ToArray();
        updateStaticPitchLayout();

        if (drawableRuleset is DrawableUtaRuleset utaRuleset)
        {
            UtaInputManager microphone = utaRuleset.KeyBindingInputManager;
            pitchDeviation.BindTo(microphone.LivePitchDeviation);
            pitchSimilarity.BindTo(microphone.LivePitchSimilarity);
            voiceActive.BindTo(microphone.LiveVoiceActive);
        }

    }

    protected override void Update()
    {
        base.Update();

        if (notes.Length == 0 || DrawHeight <= 0)
            return;

        double current = Time.Current;
        double elapsedSeconds = double.IsFinite(lastUpdateTime) && Math.Abs(current - lastUpdateTime) <= 550
            ? Math.Clamp((current - lastUpdateTime) / 1000, 0, 0.1)
            : 0;
        lastUpdateTime = current;

        float low = centreMidi - VIEW_SPAN / 2;
        float high = centreMidi + VIEW_SPAN / 2;

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
            float start = (float)((note.StartTime - current + LOOK_BEHIND) / window);
            float end = (float)((note.EndTime - current + LOOK_BEHIND) / window);
            bool visible = end >= 0 && start <= 1;
            target.Alpha = visible ? 1 : 0;
            if (!visible)
                continue;

            target.X = start;
            target.Width = Math.Max(2 / DrawWidth, end - start);
            target.Y = (high - note.Midi!.Value) / VIEW_SPAN;
        }
        previousRangeStart = rangeStart;
        previousRangeEnd = rangeEnd;

        TargetNote? active = findTargetAt(current);
        if (active != null)
        {
            active.ColourState.Accumulate(elapsedSeconds, voiceActive.Value, pitchSimilarity.Value, pitchDeviation.Value);
            active.PreviewColour();
        }

        while (nextCommitIndex < commitOrder.Length && current > commitOrder[nextCommitIndex].Note.EndTime)
        {
            commitOrder[nextCommitIndex].CommitColour();
            nextCommitIndex++;
        }

    }

    private void updateStaticPitchLayout()
    {
        float high = centreMidi + VIEW_SPAN / 2;
        int firstGridMidi = (int)MathF.Floor(centreMidi - VIEW_SPAN / 2);

        for (int i = 0; i < gridLines.Count; i++)
        {
            int midi = firstGridMidi + i;
            int pitchClass = ((midi % 12) + 12) % 12;
            bool labelled = pitchLabels.ContainsKey(midi);
            gridLines[i].Y = (high - midi) / VIEW_SPAN;
            gridLines[i].Height = labelled ? 1.2f : 0.65f;
            gridLines[i].Alpha = labelled ? 0.16f : pitchClass is 1 or 3 or 6 or 8 or 10 ? 0.032f : 0.055f;
        }

        foreach (var (midi, label) in pitchLabels)
        {
            float y = (high - midi) / VIEW_SPAN;
            label.Y = y;
            label.Alpha = y is >= 0.035f and <= 0.965f ? 0.66f : 0;
        }
    }

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

    private TargetNote? findTargetAt(double time)
    {
        for (int i = upperBoundStart(time) - 1; i >= 0 && notes[i].StartTime <= time; i--)
        {
            if (notes[i].EndTime >= time)
                return targetNotes[i];

            // Pitch notes are normally non-overlapping. This keeps overlap
            // support without turning the common case into a full-song scan.
            if (time - notes[i].StartTime > maximumNoteDuration)
                break;
        }

        return null;
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
        pitchDeviation.UnbindAll();
        pitchSimilarity.UnbindAll();
        voiceActive.UnbindAll();
        base.Dispose(isDisposing);
    }

    private partial class TargetNote : CircularContainer
    {
        public UtaNote Note { get; }
        public UtaNoteColourState ColourState { get; } = new();
        public bool ColourCommitted { get; private set; }

        private readonly Box fill;
        private UtaNoteColourGrade? previewGrade;

        public TargetNote(UtaNote note)
        {
            Note = note;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.CentreLeft;
            RelativePositionAxes = Axes.Both;
            RelativeSizeAxes = Axes.X;
            Height = 9;
            Alpha = 0;
            Masking = true;
            BorderThickness = 1;

            BorderColour = new Color4(target_pitch.R, target_pitch.G, target_pitch.B, 0.68f);
            Child = fill = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = target_pitch,
                Alpha = note.NoteKind == "freestyle" ? 0.25f : 0.42f,
            };
        }

        public void CommitColour()
        {
            if (ColourCommitted)
                return;

            ColourCommitted = true;
            UtaNoteColourGrade grade = ColourState.Grade() ?? UtaNoteColourGrade.Miss;
            (Color4 colour, float alpha, float glow) = styleFor(grade);

            fill.FadeColour(colour, 180, Easing.OutQuint);
            fill.FadeTo(alpha, 180, Easing.OutQuint);
            BorderColour = colour;
            EdgeEffect = glow > 0
                ? new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = new Color4(colour.R, colour.G, colour.B, grade == UtaNoteColourGrade.Perfect ? 0.45f : 0.24f),
                    Radius = glow,
                }
                : default;
        }

        public void PreviewColour()
        {
            UtaNoteColourGrade? grade = ColourState.Grade();
            if (grade == null || grade == previewGrade)
                return;

            previewGrade = grade;
            (Color4 colour, float alpha, _) = styleFor(grade.Value);
            fill.FadeColour(colour, 140, Easing.OutQuint);
            fill.FadeTo(alpha * 0.58f, 140, Easing.OutQuint);
        }

        private static (Color4 Colour, float Alpha, float Glow) styleFor(UtaNoteColourGrade grade)
            => grade switch
            {
                UtaNoteColourGrade.Perfect => (new Color4(253, 224, 71, 255), 0.88f, 8),
                UtaNoteColourGrade.Good => (new Color4(74, 222, 128, 255), 0.76f, 3),
                UtaNoteColourGrade.High => (new Color4(251, 146, 60, 255), 0.68f, 0),
                UtaNoteColourGrade.Low => (new Color4(96, 165, 250, 255), 0.68f, 0),
                _ => (new Color4(251, 113, 133, 255), 0.58f, 0),
            };
    }
}
