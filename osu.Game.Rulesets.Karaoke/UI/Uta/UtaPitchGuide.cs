// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

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
using osu.Game.Rulesets.Karaoke.Beatmaps;
using osu.Game.Rulesets.Karaoke.Objects;
using osu.Game.Rulesets.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Karaoke.UI.Uta;

/// <summary>
/// Uta's flowing pitch guide, using the same 1.75s history / 5.25s look-ahead
/// window and nineteen-semitone adaptive viewport as the standalone player.
/// </summary>
public partial class UtaPitchGuide : CompositeDrawable
{
    private const double look_behind = 1750;
    private const double look_ahead = 5250;
    private const double window = look_behind + look_ahead;
    private const float view_span = 19;
    private const float base_low_midi = 48;
    private const float base_high_midi = 67;
    private const float edge_margin = 1.5f;
    private const float view_move_rate = 2.4f;
    private const float playhead_position = (float)(look_behind / window);
    private const float axis_width = 50;
    private const double trace_gap = 140;

    private readonly Container noteLayer;
    private readonly Container gridLayer;
    private readonly Container traceLayer;
    private readonly Container axisLayer;
    private readonly CircularContainer voiceMarker;
    private readonly CircularContainer feedbackPill;
    private readonly Box feedbackBackground;
    private readonly OsuSpriteText feedbackText;
    private readonly List<TargetNote> targetNotes = new();
    private readonly List<Box> gridLines = new();
    private readonly List<Box> traceSegments = new();
    private readonly List<PitchSample> traceSamples = new();
    private readonly Dictionary<int, OsuSpriteText> pitchLabels = new();
    private readonly BindableFloat pitchDeviation = new();
    private readonly BindableFloat detectedPitchMidi = new();
    private readonly BindableFloat pitchSimilarity = new();
    private readonly BindableBool voiceActive = new();

    private Note[] notes = Array.Empty<Note>();
    private float centreMidi = (base_low_midi + base_high_midi) / 2;
    private double lastTraceSampleTime = double.NegativeInfinity;
    private double lastUpdateTime = double.NegativeInfinity;

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
            gridLayer = new Container { RelativeSizeAxes = Axes.Both },
            noteLayer = new Container { RelativeSizeAxes = Axes.Both },
            traceLayer = new Container { RelativeSizeAxes = Axes.Both },
            new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopCentre,
                RelativePositionAxes = Axes.X,
                X = playhead_position,
                RelativeSizeAxes = Axes.Y,
                Width = 2,
                Colour = new Color4(198, 178, 255, 255),
                Alpha = 0.72f,
            },
            voiceMarker = new CircularContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.Centre,
                RelativePositionAxes = Axes.Both,
                X = playhead_position,
                Width = 32,
                Height = 6,
                Masking = true,
                Alpha = 0,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = new Color4(105, 226, 255, 100),
                    Radius = 7,
                },
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(105, 226, 255, 255),
                },
            },
            feedbackPill = new CircularContainer
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.CentreLeft,
                RelativePositionAxes = Axes.Both,
                X = playhead_position + 0.014f,
                Width = 92,
                Height = 24,
                Masking = true,
                Alpha = 0,
                Children = new Drawable[]
                {
                    feedbackBackground = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(13, 20, 32, 255),
                        Alpha = 0.56f,
                    },
                    feedbackText = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.Default.With(size: 10.5f, weight: FontWeight.Bold),
                    },
                },
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
                        Colour = new Color4(8, 14, 24, 255),
                        Alpha = 0.12f,
                    },
                    new Box
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        RelativeSizeAxes = Axes.Y,
                        Width = 1,
                        Colour = Color4.White,
                        Alpha = 0.08f,
                    },
                },
            },
            new OsuSpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Margin = new MarginPadding { Left = axis_width + 11, Top = 9 },
                Text = "PITCH  •  LIVE",
                Font = OsuFont.Default.With(size: 10, weight: FontWeight.Bold),
                Colour = new Color4(196, 183, 255, 255),
                Alpha = 0.76f,
            },
        };

        for (int i = 0; i <= (int)view_span; i++)
        {
            var line = new Box
            {
                RelativePositionAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
                Height = i % 12 == 0 ? 1.2f : 0.65f,
                Colour = Color4.White,
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
                Colour = new Color4(224, 226, 240, 255),
                Alpha = 0,
            };
            pitchLabels.Add(midi, label);
            axisLayer.Add(label);
        }
    }

    [BackgroundDependencyLoader]
    private void load(KaraokeBeatmap beatmap, DrawableRuleset drawableRuleset)
    {
        if (beatmap.UtaPackageId == null)
        {
            Hide();
            return;
        }

        notes = beatmap.HitObjects.OfType<Note>()
                       .Where(note => note.Midi != null && note.Display)
                       .OrderBy(note => note.StartTime)
                       .ToArray();

        foreach (var note in notes)
        {
            var drawable = new TargetNote(note);
            targetNotes.Add(drawable);
            noteLayer.Add(drawable);
        }

        if (drawableRuleset is not DrawableKaraokeRuleset karaokeRuleset)
            return;

        KaraokeInputManager microphone = karaokeRuleset.KeyBindingInputManager;
        pitchDeviation.BindTo(microphone.LivePitchDeviation);
        detectedPitchMidi.BindTo(microphone.LiveDetectedPitchMidi);
        pitchSimilarity.BindTo(microphone.LivePitchSimilarity);
        voiceActive.BindTo(microphone.LiveVoiceActive);
        voiceActive.BindValueChanged(active => voiceMarker.FadeTo(active.NewValue ? 1 : 0, 90, Easing.OutQuint), true);
    }

    protected override void Update()
    {
        base.Update();

        if (notes.Length == 0 || DrawHeight <= 0)
            return;

        double current = Time.Current;

        if (double.IsFinite(lastUpdateTime) && Math.Abs(current - lastUpdateTime) > 550)
            clearTrace();
        lastUpdateTime = current;

        updateViewport(current);
        float low = centreMidi - view_span / 2;
        float high = centreMidi + view_span / 2;

        int firstGridMidi = (int)MathF.Floor(low);
        for (int i = 0; i < gridLines.Count; i++)
        {
            int midi = firstGridMidi + i;
            int pitchClass = ((midi % 12) + 12) % 12;
            bool labelled = pitchLabels.ContainsKey(midi);
            gridLines[i].Y = (high - midi) / view_span;
            gridLines[i].Height = labelled ? 1.2f : 0.65f;
            gridLines[i].Alpha = labelled ? 0.16f : pitchClass is 1 or 3 or 6 or 8 or 10 ? 0.032f : 0.055f;
        }

        foreach (var (midi, label) in pitchLabels)
        {
            float y = (high - midi) / view_span;
            label.Y = y;
            label.Alpha = y is >= 0.035f and <= 0.965f ? 0.66f : 0;
        }

        foreach (var target in targetNotes)
        {
            var note = target.Note;
            float start = (float)((note.StartTime - current + look_behind) / window);
            float end = (float)((note.EndTime - current + look_behind) / window);
            bool visible = end >= 0 && start <= 1;
            target.Alpha = visible ? 1 : 0;
            if (!visible)
                continue;

            target.X = start;
            target.Width = Math.Max(2 / DrawWidth, end - start);
            target.Y = (high - note.Midi!.Value) / view_span;
        }

        Note? active = findNoteAt(current);
        updateTrace(current, low, high);

        if (voiceActive.Value)
            voiceMarker.Y = Math.Clamp((high - detectedPitchMidi.Value) / view_span, 0, 1);

        updateFeedback(active, high);
    }

    private void updateTrace(double current, float low, float high)
    {
        if (voiceActive.Value && current - lastTraceSampleTime >= 20)
        {
            traceSamples.Add(new PitchSample(current, detectedPitchMidi.Value, pitchSimilarity.Value));
            lastTraceSampleTime = current;
        }

        traceSamples.RemoveAll(sample => sample.Time < current - look_behind - 200 || sample.Time > current + 100);

        int segmentIndex = 0;
        for (int i = 1; i < traceSamples.Count; i++)
        {
            PitchSample previous = traceSamples[i - 1];
            PitchSample sample = traceSamples[i];
            if (sample.Time - previous.Time > trace_gap || Math.Abs(sample.Midi - previous.Midi) > 5.5f)
                continue;

            float x1 = (float)((previous.Time - current + look_behind) / window) * DrawWidth;
            float x2 = (float)((sample.Time - current + look_behind) / window) * DrawWidth;
            float y1 = (high - previous.Midi) / view_span * DrawHeight;
            float y2 = (high - sample.Midi) / view_span * DrawHeight;
            if (x2 < axis_width || x1 > DrawWidth || (y1 < 0 && y2 < 0) || (y1 > DrawHeight && y2 > DrawHeight))
                continue;

            Vector2 start = new(x1, y1);
            Vector2 delta = new Vector2(x2, y2) - start;
            Box segment = getTraceSegment(segmentIndex++);
            segment.Position = start;
            segment.Width = Math.Max(1, delta.Length);
            segment.Rotation = MathF.Atan2(delta.Y, delta.X) * 180 / MathF.PI;
            segment.Colour = traceColour((previous.Similarity + sample.Similarity) / 2, findNoteAt(sample.Time) != null);
            segment.Alpha = 1;
        }

        for (int i = segmentIndex; i < traceSegments.Count; i++)
            traceSegments[i].Alpha = 0;
    }

    private Box getTraceSegment(int index)
    {
        while (traceSegments.Count <= index)
        {
            var segment = new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.CentreLeft,
                Height = 3.8f,
                Alpha = 0,
            };
            traceSegments.Add(segment);
            traceLayer.Add(segment);
        }

        return traceSegments[index];
    }

    private void updateFeedback(Note? active, float high)
    {
        if (active?.Midi is not { } targetMidi)
        {
            feedbackPill.Alpha = 0;
            return;
        }

        float midi = voiceActive.Value ? detectedPitchMidi.Value : targetMidi;
        feedbackPill.Y = Math.Clamp((high - midi) / view_span, 0.1f, 0.9f);
        feedbackPill.Alpha = 1;

        if (!voiceActive.Value)
        {
            setFeedback("SING", new Color4(203, 213, 225, 255));
            return;
        }

        float error = pitchDeviation.Value;
        if (Math.Abs(error) <= 0.35f)
            setFeedback("PERFECT", new Color4(254, 240, 138, 255));
        else if (Math.Abs(error) <= 0.75f)
            setFeedback("GOOD", new Color4(134, 239, 172, 255));
        else if (error > 0)
            setFeedback("HIGH ↓", new Color4(253, 186, 116, 255));
        else
            setFeedback("LOW ↑", new Color4(147, 197, 253, 255));
    }

    private void setFeedback(string text, Color4 colour)
    {
        feedbackText.Text = text;
        feedbackText.Colour = colour;
        feedbackBackground.Colour = colour;
        feedbackBackground.Alpha = 0.20f;
        voiceMarker.Child.Colour = colour;
    }

    private static Color4 traceColour(float similarity, bool hasTarget)
    {
        if (!hasTarget)
            return new Color4(82, 225, 255, 255);
        if (similarity >= 0.94f)
            return new Color4(254, 240, 138, 255);
        if (similarity >= 0.7f)
            return new Color4(134, 239, 172, 255);
        if (similarity >= 0.3f)
            return new Color4(251, 146, 60, 255);
        return new Color4(251, 113, 133, 255);
    }

    private void clearTrace()
    {
        traceSamples.Clear();
        lastTraceSampleTime = double.NegativeInfinity;
        foreach (var segment in traceSegments)
            segment.Alpha = 0;
    }

    private static string midiName(int midi)
    {
        string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        return $"{names[((midi % 12) + 12) % 12]}{midi / 12 - 1}";
    }

    private void updateViewport(double current)
    {
        float targetCentre = (base_low_midi + base_high_midi) / 2;
        var upcoming = notes.Where(note => note.EndTime >= current - 200 && note.StartTime <= current + look_ahead * 0.72).ToArray();

        if (upcoming.Length > 0)
        {
            float low = upcoming.Min(note => (float)note.Midi!.Value);
            float high = upcoming.Max(note => (float)note.Midi!.Value);

            if (high - low > view_span - edge_margin * 2)
                targetCentre = (low + high) / 2;
            else if (low < base_low_midi)
                targetCentre = low - edge_margin + view_span / 2;
            else if (high > base_high_midi)
                targetCentre = high + edge_margin - view_span / 2;
        }

        targetCentre = MathF.Round(Math.Clamp(targetCentre, 40 + view_span / 2, 88 - view_span / 2) * 2) / 2;
        float dt = Math.Clamp((float)(Time.Elapsed / 1000), 0, 0.05f);
        float alpha = 1 - MathF.Exp(-dt / 0.85f);
        float desired = (targetCentre - centreMidi) * alpha;

        if (Math.Abs(targetCentre - centreMidi) >= 0.2f)
            centreMidi += Math.Clamp(desired, -view_move_rate * dt, view_move_rate * dt);
    }

    private Note? findNoteAt(double time)
    {
        int low = 0;
        int high = notes.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            Note note = notes[middle];
            if (time < note.StartTime)
                high = middle - 1;
            else if (time > note.EndTime)
                low = middle + 1;
            else
                return note;
        }

        return null;
    }

    protected override void Dispose(bool isDisposing)
    {
        pitchDeviation.UnbindAll();
        detectedPitchMidi.UnbindAll();
        pitchSimilarity.UnbindAll();
        voiceActive.UnbindAll();
        base.Dispose(isDisposing);
    }

    private partial class TargetNote : CircularContainer
    {
        public Note Note { get; }

        public TargetNote(Note note)
        {
            Note = note;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.CentreLeft;
            RelativePositionAxes = Axes.Both;
            RelativeSizeAxes = Axes.X;
            Height = 9;
            Masking = true;
            BorderThickness = 1;

            Color4 colour = note.NoteKind switch
            {
                "golden" or "golden_rap" => new Color4(250, 204, 21, 255),
                "rap" => new Color4(192, 132, 252, 255),
                "freestyle" => new Color4(148, 163, 184, 255),
                _ => new Color4(92, 218, 255, 255),
            };

            BorderColour = new Color4(colour.R, colour.G, colour.B, 0.8f);
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colour,
                Alpha = 0.28f,
            };
        }
    }

    private readonly record struct PitchSample(double Time, float Midi, float Similarity);
}
