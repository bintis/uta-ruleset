// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Rulesets.Uta.Pitch;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// A compact rolling pitch history graph. Its buffer layout, age fading and
/// reference/user colour mapping are adapted from rzru/nightingale's
/// pitch-graph.tsx (GPL-3.0-or-later) to osu!framework drawables.
/// </summary>
internal sealed partial class UtaPitchCurveGraph : CompositeDrawable
{
    private const int buffer_size = 200;
    private const double sample_interval = 20;
    private const float line_width = 2.25f;

    private static readonly Color4 reference_colour = new(128, 179, 255, 255);
    private static readonly Color4 user_base_colour = new(217, 217, 255, 255);
    private static readonly Color4 similarity_good = new(51, 230, 77, 255);
    private static readonly Color4 similarity_ok = new(242, 204, 26, 255);
    private static readonly Color4 similarity_bad = new(230, 51, 51, 255);

    private readonly Container referenceLayer;
    private readonly Container userLayer;
    private readonly List<CurveSample> samples = new(buffer_size);
    private readonly List<CurveSegment> referenceSegments = new();
    private readonly List<CurveSegment> userSegments = new();
    private readonly Bindable<UtaPitchCurveDisplay> display = new();
    private readonly BindableFloat detectedPitchMidi = new();
    private readonly BindableFloat pitchSimilarity = new();
    private readonly BindableBool voiceActive = new();

    private UtaNote[] notes = Array.Empty<UtaNote>();
    private ReferenceFrame[] referenceFrames = Array.Empty<ReferenceFrame>();
    private float centreMidi;
    private double lastSampleTime = double.NegativeInfinity;
    private double lastPlaybackTime = double.NegativeInfinity;

    public UtaPitchCurveGraph()
    {
        RelativeSizeAxes = Axes.Both;

        InternalChildren = new Drawable[]
        {
            referenceLayer = new Container { RelativeSizeAxes = Axes.Both },
            userLayer = new Container { RelativeSizeAxes = Axes.Both },
        };
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap, DrawableRuleset drawableRuleset, UtaRulesetConfigManager config,
                      IBindable<WorkingBeatmap> workingBeatmap)
    {
        notes = beatmap.HitObjects.OfType<UtaNote>()
                       .Where(note => note.Midi != null)
                       .OrderBy(note => note.StartTime)
                       .ToArray();
        centreMidi = UtaPitchGuide.CalculateFixedCentre(notes);
        referenceFrames = loadReferenceFrames(workingBeatmap.Value);
        display.BindTo(config.GetBindable<UtaPitchCurveDisplay>(UtaRulesetSetting.PitchCurveDisplay));

        if (drawableRuleset is not DrawableUtaRuleset utaRuleset)
            return;

        UtaInputManager microphone = utaRuleset.KeyBindingInputManager;
        detectedPitchMidi.BindTo(microphone.LiveDetectedPitchMidi);
        pitchSimilarity.BindTo(microphone.LivePitchSimilarity);
        voiceActive.BindTo(microphone.LiveVoiceActive);
    }

    protected override void Update()
    {
        base.Update();

        UtaPitchCurveDisplay mode = display.Value;
        Alpha = mode == UtaPitchCurveDisplay.Off ? 0 : 1;

        double current = Time.Current;
        if (double.IsFinite(lastPlaybackTime) && Math.Abs(current - lastPlaybackTime) > 550)
            clearSamples();
        lastPlaybackTime = current;

        if (current - lastSampleTime >= sample_interval)
        {
            UtaNote? target = findNoteAt(current);
            samples.Add(new CurveSample(
                current,
                target?.Midi,
                voiceActive.Value ? detectedPitchMidi.Value : null,
                pitchSimilarity.Value));
            if (samples.Count > buffer_size)
                samples.RemoveAt(0);
            lastSampleTime = current;
        }

        bool showSong = mode is UtaPitchCurveDisplay.Song or UtaPitchCurveDisplay.Both;
        bool showVoice = mode is UtaPitchCurveDisplay.MyVoice or UtaPitchCurveDisplay.Both;
        drawReference(showSong, current);
        drawUser(showVoice, current);
    }

    private void drawReference(bool visible, double currentTime)
    {
        int used = 0;
        if (visible)
        {
            double visibleStart = currentTime - UtaPitchGuide.LOOK_BEHIND;
            double visibleEnd = currentTime + UtaPitchGuide.LOOK_AHEAD;

            if (referenceFrames.Length > 0)
            {
                int first = Math.Max(1, lowerBoundReferenceFrame(visibleStart) - 1);
                for (int i = first; i < referenceFrames.Length && referenceFrames[i - 1].Time <= visibleEnd; i++)
                {
                    ReferenceFrame previous = referenceFrames[i - 1];
                    ReferenceFrame frame = referenceFrames[i];
                    if (frame.Time - previous.Time > 140 || Math.Abs(frame.Midi - previous.Midi) > 5.5f)
                        continue;

                    if (setSegment(getSegment(referenceSegments, referenceLayer, used), previous.Time, frame.Time,
                                   currentTime, previous.Midi, frame.Midi, reference_colour, 0.30f))
                        used++;
                }
            }
            else
            {
                foreach (UtaNote note in notes)
                {
                    if (note.EndTime < visibleStart || note.StartTime > visibleEnd || note.Midi is not { } midi)
                        continue;

                    if (setSegment(getSegment(referenceSegments, referenceLayer, used), note.StartTime, note.EndTime,
                                   currentTime, midi, midi, reference_colour, 0.30f))
                        used++;
                }
            }
        }

        hideUnused(referenceSegments, used);
    }

    private void drawUser(bool visible, double currentTime)
    {
        int used = 0;
        if (visible)
        {
            for (int i = 1; i < samples.Count; i++)
            {
                CurveSample previous = samples[i - 1];
                CurveSample current = samples[i];
                if (previous.UserMidi is not { } from || current.UserMidi is not { } to)
                    continue;

                (Color4 previousColour, float previousWeight) = userStyle(previous);
                (Color4 currentColour, float currentWeight) = userStyle(current);
                Color4 colour = blend(previousColour, currentColour, 0.5f);
                float alpha = (previousWeight * AgeAlpha(i - 1, samples.Count)
                               + currentWeight * AgeAlpha(i, samples.Count)) / 2;
                if (setSegment(getSegment(userSegments, userLayer, used), previous.Time, current.Time,
                               currentTime, from, to, colour, alpha))
                    used++;
            }
        }

        hideUnused(userSegments, used);
    }

    private bool setSegment(CurveSegment segment, double fromTime, double toTime, double currentTime,
                            float fromMidi, float toMidi, Color4 colour, float alpha)
    {
        Vector2 start = new(TimeToX(fromTime, currentTime, DrawWidth), MidiToY(fromMidi, centreMidi, DrawHeight));
        Vector2 end = new(TimeToX(toTime, currentTime, DrawWidth), MidiToY(toMidi, centreMidi, DrawHeight));
        if (end.X < 0 || start.X > DrawWidth || (start.Y < 0 && end.Y < 0) || (start.Y > DrawHeight && end.Y > DrawHeight))
        {
            segment.Alpha = 0;
            return false;
        }

        Vector2 delta = end - start;
        segment.Position = start;
        segment.Width = Math.Max(line_width, delta.Length);
        segment.Rotation = MathF.Atan2(delta.Y, delta.X) * 180 / MathF.PI;
        segment.Colour = colour;
        segment.Alpha = alpha;
        return true;
    }

    internal static float MidiToY(float midi, float centre, float height)
        => (centre + UtaPitchGuide.VIEW_SPAN / 2 - midi) / UtaPitchGuide.VIEW_SPAN * height;

    private static (Color4 Colour, float Weight) userStyle(CurveSample sample)
    {
        if (sample.ReferenceMidi == null)
            return (user_base_colour, 0.55f);

        float similarity = Math.Clamp(sample.Similarity, 0, 1);
        Color4 quality = similarity >= 0.7f
            ? blend(similarity_ok, similarity_good, (similarity - 0.7f) / 0.3f)
            : blend(similarity_bad, similarity_ok, similarity / 0.7f);
        return (blend(user_base_colour, quality, similarity), 0.35f + similarity * 0.65f);
    }

    internal static float AgeAlpha(int index, int seriesLength)
    {
        float t = index / (float)Math.Max(1, seriesLength - 1);
        return 0.25f + 0.75f * t;
    }

    internal static float TimeToX(double sampleTime, double currentTime, float width)
        => (float)((sampleTime - currentTime + UtaPitchGuide.LOOK_BEHIND)
                   / (UtaPitchGuide.LOOK_BEHIND + UtaPitchGuide.LOOK_AHEAD)) * width;

    private static Color4 blend(Color4 from, Color4 to, float amount) => new(
        from.R + (to.R - from.R) * amount,
        from.G + (to.G - from.G) * amount,
        from.B + (to.B - from.B) * amount,
        1);

    private static CurveSegment getSegment(List<CurveSegment> segments, Container layer, int index)
    {
        while (segments.Count <= index)
        {
            var segment = new CurveSegment();
            segments.Add(segment);
            layer.Add(segment);
        }

        return segments[index];
    }

    private static void hideUnused(List<CurveSegment> segments, int used)
    {
        for (int i = used; i < segments.Count; i++)
            segments[i].Alpha = 0;
    }

    private UtaNote? findNoteAt(double time)
    {
        int low = 0;
        int high = notes.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            UtaNote note = notes[middle];
            if (time < note.StartTime)
                high = middle - 1;
            else if (time > note.EndTime)
                low = middle + 1;
            else
                return note;
        }

        return null;
    }

    private int lowerBoundReferenceFrame(double time)
    {
        int low = 0;
        int high = referenceFrames.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (referenceFrames[middle].Time < time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static ReferenceFrame[] loadReferenceFrames(WorkingBeatmap working)
    {
        try
        {
            string? manifestPath = working.BeatmapSetInfo.GetPathForFile("manifest.json");
            if (manifestPath == null)
                return Array.Empty<ReferenceFrame>();

            using Stream manifestStream = working.GetStream(manifestPath);
            UtzManifest? manifest = JsonSerializer.Deserialize<UtzManifest>(manifestStream, UtzPackage.JsonOptions);
            string? pitchPath = manifest?.Charts.PitchTrack.Path;
            string? pitchStoragePath = pitchPath == null ? null : working.BeatmapSetInfo.GetPathForFile(pitchPath);
            if (pitchStoragePath == null)
                return Array.Empty<ReferenceFrame>();

            using Stream pitchStream = working.GetStream(pitchStoragePath);
            UtaPitchTrack? track = JsonSerializer.Deserialize<UtaPitchTrack>(pitchStream, UtzPackage.JsonOptions);
            if (track == null)
                return Array.Empty<ReferenceFrame>();

            return track.Frames.Where(frame => frame.Hertz is { } hertz && UtaPitchMath.IsFinitePitch(hertz))
                        .Select(frame => new ReferenceFrame(frame.Time * 1000,
                            (float)UtaPitchMath.FrequencyToMidi(frame.Hertz!.Value)))
                        .ToArray();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            Logger.Log($"Uta could not load the song pitch-analysis curve: {ex.GetBaseException().Message}");
            return Array.Empty<ReferenceFrame>();
        }
    }

    private void clearSamples()
    {
        samples.Clear();
        lastSampleTime = double.NegativeInfinity;
        hideUnused(referenceSegments, 0);
        hideUnused(userSegments, 0);
    }

    protected override void Dispose(bool isDisposing)
    {
        display.UnbindAll();
        detectedPitchMidi.UnbindAll();
        pitchSimilarity.UnbindAll();
        voiceActive.UnbindAll();
        base.Dispose(isDisposing);
    }

    private sealed partial class CurveSegment : CircularContainer
    {
        public CurveSegment()
        {
            Anchor = Anchor.TopLeft;
            Origin = Anchor.CentreLeft;
            Height = line_width;
            Masking = true;
            Child = new Box { RelativeSizeAxes = Axes.Both };
        }
    }

    private readonly record struct CurveSample(double Time, float? ReferenceMidi, float? UserMidi, float Similarity);
    private readonly record struct ReferenceFrame(double Time, float Midi);
}
