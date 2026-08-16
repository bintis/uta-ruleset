// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Optional thick, glowing microphone trail from the earlier pitch guide.
/// This remains separate from the thinner Nightingale analysis curves.
/// </summary>
internal sealed partial class UtaPitchGuideTrail : CompositeDrawable
{
    private const double sample_interval = 30;
    private const double trace_gap = 140;

    private static readonly Color4 accurate_voice = new(112, 242, 211, 255);
    private static readonly Color4 close_voice = new(241, 181, 98, 255);
    private static readonly Color4 inaccurate_voice = new(226, 117, 73, 255);

    private readonly Container glowLayer;
    private readonly Container traceLayer;
    private readonly List<Box> glowSegments = new();
    private readonly List<Box> traceSegments = new();
    private readonly List<TrailSample> samples = new();
    private readonly BindableBool enabled = new();
    private readonly BindableFloat detectedPitchMidi = new();
    private readonly BindableFloat pitchSimilarity = new();
    private readonly BindableBool voiceActive = new();
    private readonly BindableFloat keyShiftSemitones = new();
    private readonly BindableDouble detectedPitchTime = new();

    private UtaNote[] notes = Array.Empty<UtaNote>();
    private readonly BindableFloat centreMidi = new();
    private double timelineEndTime;
    private double lastSampleTime = double.NegativeInfinity;
    private double lastPlaybackTime = double.NegativeInfinity;
    private bool geometryReady;
    private float geometryWidth = -1;
    private float geometryHeight = -1;
    private GameplayClockContainer? gameplayClock;

    public UtaPitchGuideTrail()
    {
        RelativeSizeAxes = Axes.Both;
        InternalChildren = new Drawable[]
        {
            glowLayer = new Container { RelativeSizeAxes = Axes.Y },
            traceLayer = new Container { RelativeSizeAxes = Axes.Y },
        };
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap, DrawableRuleset drawableRuleset, UtaRulesetConfigManager config, GameplayClockContainer gameplayClock,
                      UtaPitchViewport pitchViewport)
    {
        this.gameplayClock = gameplayClock;
        gameplayClock.OnSeek += onSeek;
        notes = beatmap.HitObjects.OfType<UtaNote>()
                       .Where(note => note.Midi != null)
                       .OrderBy(note => note.StartTime)
                       .ToArray();
        centreMidi.BindTo(pitchViewport.CentreMidi);
        timelineEndTime = (notes.Length > 0 ? notes[^1].EndTime : 0) + UtaPitchGuide.LOOK_AHEAD;
        enabled.BindTo(config.GetBindable<bool>(UtaRulesetSetting.ShowPitchGuideTrail));
        keyShiftSemitones.BindTo(config.GetBindable<float>(UtaRulesetSetting.KeyShiftSemitones));

        if (drawableRuleset is not DrawableUtaRuleset utaRuleset)
            return;

        UtaInputManager microphone = utaRuleset.KeyBindingInputManager;
        detectedPitchMidi.BindTo(microphone.LiveDetectedPitchMidi);
        pitchSimilarity.BindTo(microphone.LivePitchSimilarity);
        voiceActive.BindTo(microphone.LiveVoiceActive);
        detectedPitchTime.BindTo(microphone.LiveDetectedPitchTime);
    }

    protected override void Update()
    {
        base.Update();
        Alpha = enabled.Value ? 1 : 0;
        if (!enabled.Value)
            return;

        double current = Time.Current;
        if (double.IsFinite(lastPlaybackTime) && Math.Abs(current - lastPlaybackTime) > 550)
            clear();
        lastPlaybackTime = current;

        bool samplesChanged = false;
        double sampleTime = detectedPitchTime.Value;
        if (voiceActive.Value && sampleTime - lastSampleTime >= sample_interval)
        {
            samples.Add(new TrailSample(sampleTime, detectedPitchMidi.Value, pitchSimilarity.Value));
            lastSampleTime = sampleTime;
            samplesChanged = true;
        }

        int removeCount = 0;
        double oldestVisibleTime = current - UtaPitchGuide.LOOK_BEHIND - 200;
        while (removeCount < samples.Count && samples[removeCount].Time < oldestVisibleTime)
            removeCount++;

        if (removeCount > 0)
        {
            samples.RemoveRange(0, removeCount);
            samplesChanged = true;
        }

        // Future samples only remain after a backwards seek. Remove them
        // without a capturing predicate so normal frames allocate nothing.
        int futureStart = samples.Count;
        while (futureStart > 0 && samples[futureStart - 1].Time > current + 100)
            futureStart--;

        if (futureStart < samples.Count)
        {
            samples.RemoveRange(futureStart, samples.Count - futureStart);
            samplesChanged = true;
        }

        bool sizeChanged = DrawWidth != geometryWidth || DrawHeight != geometryHeight;
        if (sizeChanged)
        {
            float timelineWidth = Math.Max(DrawWidth, UtaPitchCurveGraph.TimeToX(timelineEndTime, 0, DrawWidth));
            glowLayer.Width = timelineWidth;
            traceLayer.Width = timelineWidth;
        }
        glowLayer.X = UtaPitchCurveGraph.TimeOffsetToX(0, current, DrawWidth);
        traceLayer.X = glowLayer.X;
        if (samplesChanged || sizeChanged || !geometryReady)
        {
            draw(0);
            geometryReady = true;
        }

        geometryWidth = DrawWidth;
        geometryHeight = DrawHeight;
    }

    private void draw(double current)
    {
        int used = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            TrailSample previous = samples[i - 1];
            TrailSample sample = samples[i];
            if (sample.Time - previous.Time > trace_gap || Math.Abs(sample.Midi - previous.Midi) > 5.5f)
                continue;

            Vector2 start = new(
                UtaPitchCurveGraph.TimeToX(previous.Time, current, DrawWidth),
                UtaPitchCurveGraph.MidiToY(previous.Midi, centreMidi.Value + keyShiftSemitones.Value, DrawHeight));
            Vector2 end = new(
                UtaPitchCurveGraph.TimeToX(sample.Time, current, DrawWidth),
                UtaPitchCurveGraph.MidiToY(sample.Midi, centreMidi.Value + keyShiftSemitones.Value, DrawHeight));
            if ((start.Y < 0 && end.Y < 0) || (start.Y > DrawHeight && end.Y > DrawHeight))
                continue;

            Vector2 delta = end - start;
            float similarity = (previous.Similarity + sample.Similarity) / 2;
            Color4 colour = trailColour(similarity, findNoteAt(sample.Time) != null);

            Box segment = getSegment(traceSegments, traceLayer, used);
            setSegment(segment, start, delta, colour, 3.2f + similarity * 1.6f, 0.76f + similarity * 0.24f);

            Box glow = getSegment(glowSegments, glowLayer, used);
            setSegment(glow, start, delta, colour, 8 + similarity * 4, 0.08f + similarity * 0.22f);
            used++;
        }

        hideUnused(traceSegments, used);
        hideUnused(glowSegments, used);
    }

    private static void setSegment(Box segment, Vector2 start, Vector2 delta, Color4 colour, float height, float alpha)
    {
        segment.Position = start;
        segment.Width = Math.Max(1, delta.Length);
        segment.Rotation = MathF.Atan2(delta.Y, delta.X) * 180 / MathF.PI;
        segment.Colour = colour;
        segment.Height = height;
        segment.Alpha = alpha;
    }

    private static Box getSegment(List<Box> segments, Container layer, int index)
    {
        while (segments.Count <= index)
        {
            var segment = new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.CentreLeft,
                Alpha = 0,
            };
            segments.Add(segment);
            layer.Add(segment);
        }

        return segments[index];
    }

    private static void hideUnused(List<Box> segments, int used)
    {
        for (int i = used; i < segments.Count; i++)
            segments[i].Alpha = 0;
    }

    private static Color4 trailColour(float similarity, bool hasTarget)
    {
        if (!hasTarget)
            return new Color4(105, 187, 211, 255);

        float amount = Math.Clamp(similarity, 0, 1);
        return amount < 0.65f
            ? blend(inaccurate_voice, close_voice, amount / 0.65f)
            : blend(close_voice, accurate_voice, (amount - 0.65f) / 0.35f);
    }

    private static Color4 blend(Color4 from, Color4 to, float amount) => new(
        from.R + (to.R - from.R) * amount,
        from.G + (to.G - from.G) * amount,
        from.B + (to.B - from.B) * amount,
        1);

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

    private void clear()
    {
        samples.Clear();
        lastSampleTime = double.NegativeInfinity;
        geometryReady = false;
        hideUnused(traceSegments, 0);
        hideUnused(glowSegments, 0);
    }

    private void onSeek() => clear();

    protected override void Dispose(bool isDisposing)
    {
        enabled.UnbindAll();
        detectedPitchMidi.UnbindAll();
        pitchSimilarity.UnbindAll();
        voiceActive.UnbindAll();
        keyShiftSemitones.UnbindAll();
        detectedPitchTime.UnbindAll();
        centreMidi.UnbindAll();
        if (gameplayClock != null)
            gameplayClock.OnSeek -= onSeek;
        base.Dispose(isDisposing);
    }

    private readonly record struct TrailSample(double Time, float Midi, float Similarity);
}
