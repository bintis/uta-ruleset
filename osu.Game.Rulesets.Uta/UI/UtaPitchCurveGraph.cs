// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private const double reference_rebuild_interval = 100;
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
    private readonly BindableFloat keyShiftSemitones = new();
    private readonly BindableFloat microphoneLatency = new();
    private readonly BindableDouble detectedPitchTime = new();
    private readonly BindableBool debugDiagnostics = new();

    private UtaNote[] notes = Array.Empty<UtaNote>();
    private ReferenceFrame[] referenceFrames = Array.Empty<ReferenceFrame>();
    private float centreMidi;
    private double timelineEndTime;
    private double lastSampleTime = double.NegativeInfinity;
    private double lastPlaybackTime = double.NegativeInfinity;
    private double referenceGeometryTime = double.NaN;
    private bool userGeometryReady;
    private float geometryWidth = -1;
    private float geometryHeight = -1;
    private long diagnosticIntervalStart;
    private long diagnosticUpdateTicks;
    private long diagnosticMaximumUpdateTicks;
    private int diagnosticUpdates;
    private int diagnosticSamplesAdded;
    private int diagnosticReferenceRebuilds;
    private int diagnosticUserRebuilds;
    private int diagnosticSampleClears;
    private double diagnosticMaximumPlaybackStep;
    private string diagnosticLastClearReason = "none";

    public UtaPitchCurveGraph()
    {
        RelativeSizeAxes = Axes.Both;

        InternalChildren = new Drawable[]
        {
            referenceLayer = new Container { RelativeSizeAxes = Axes.Y },
            userLayer = new Container { RelativeSizeAxes = Axes.Y },
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
        timelineEndTime = workingBeatmap.Value.Track.Length;
        if (!double.IsFinite(timelineEndTime))
            timelineEndTime = 0;
        if (notes.Length > 0)
            timelineEndTime = Math.Max(timelineEndTime, notes[^1].EndTime);
        if (referenceFrames.Length > 0)
            timelineEndTime = Math.Max(timelineEndTime, referenceFrames[^1].Time);
        timelineEndTime += UtaPitchGuide.LOOK_AHEAD;
        display.BindTo(config.GetBindable<UtaPitchCurveDisplay>(UtaRulesetSetting.PitchCurveDisplay));
        debugDiagnostics.BindTo(config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics));
        debugDiagnostics.BindValueChanged(_ => resetDiagnostics(), true);
        keyShiftSemitones.BindTo(config.GetBindable<float>(UtaRulesetSetting.KeyShiftSemitones));
        microphoneLatency.BindTo(config.GetBindable<float>(UtaRulesetSetting.MicrophoneLatency));
        microphoneLatency.BindValueChanged(_ => userGeometryReady = false);
        keyShiftSemitones.BindValueChanged(_ =>
        {
            referenceGeometryTime = double.NaN;
            userGeometryReady = false;
        });

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
        long updateStart = Stopwatch.GetTimestamp();
        base.Update();

        UtaPitchCurveDisplay mode = display.Value;
        if (mode == UtaPitchCurveDisplay.Off)
        {
            if (Alpha != 0)
            {
                Alpha = 0;
                clearSamples("display-off");
            }

            recordDiagnostics(updateStart);
            return;
        }

        Alpha = 1;

        double current = Time.Current;
        double playbackStep = double.IsFinite(lastPlaybackTime) ? current - lastPlaybackTime : 0;
        diagnosticMaximumPlaybackStep = Math.Max(diagnosticMaximumPlaybackStep, Math.Abs(playbackStep));
        if (double.IsFinite(lastPlaybackTime) && Math.Abs(playbackStep) > 550)
            clearSamples($"timeline-step={playbackStep:+0.0;-0.0;0.0}ms");
        lastPlaybackTime = current;

        bool sampleAdded = false;
        bool active = voiceActive.Value;
        double sampleTime = active ? detectedPitchTime.Value : current;
        bool shouldAddSample = active
            ? sampleTime - lastSampleTime >= sample_interval
            : samples.Count == 0 || samples[^1].UserMidi != null;
        if (shouldAddSample)
        {
            // Keep silent break markers on the same latency-adjusted timeline as voiced samples.
            // Advancing them with Time.Current would make newly detected pitch appear older than
            // the sampling gate and discard roughly one microphone-latency worth of history.
            if (!active && double.IsFinite(lastSampleTime))
                sampleTime = Math.Min(current, lastSampleTime + sample_interval);

            UtaNote? target = findNoteAt(sampleTime);
            samples.Add(new CurveSample(
                sampleTime,
                current,
                target?.Midi,
                active ? detectedPitchMidi.Value : null,
                pitchSimilarity.Value));
            if (samples.Count > buffer_size)
                samples.RemoveAt(0);

            lastSampleTime = sampleTime;
            sampleAdded = true;
            diagnosticSamplesAdded++;
        }

        bool showSong = mode is UtaPitchCurveDisplay.Song or UtaPitchCurveDisplay.Both;
        bool showVoice = mode is UtaPitchCurveDisplay.MyVoice or UtaPitchCurveDisplay.Both;
        bool sizeChanged = DrawWidth != geometryWidth || DrawHeight != geometryHeight;
        if (sizeChanged)
        {
            float timelineWidth = Math.Max(DrawWidth, TimeToX(timelineEndTime, 0, DrawWidth));
            referenceLayer.Width = timelineWidth;
            userLayer.Width = timelineWidth;
        }

        if (showSong)
        {
            referenceLayer.X = TimeOffsetToX(0, current, DrawWidth);
            if (sizeChanged || !double.IsFinite(referenceGeometryTime) || Math.Abs(current - referenceGeometryTime) >= reference_rebuild_interval)
            {
                drawReference(true, current);
                referenceGeometryTime = current;
                diagnosticReferenceRebuilds++;
            }
        }
        else
        {
            referenceLayer.X = 0;
            referenceGeometryTime = double.NaN;
            drawReference(false, current);
        }

        if (showVoice)
        {
            userLayer.X = TimeOffsetToX(0, current, DrawWidth);
            if (sampleAdded || sizeChanged || !userGeometryReady)
            {
                drawUser(true);
                userGeometryReady = true;
                diagnosticUserRebuilds++;
            }
        }
        else
        {
            userLayer.X = 0;
            userGeometryReady = false;
            drawUser(false);
        }

        geometryWidth = DrawWidth;
        geometryHeight = DrawHeight;
        recordDiagnostics(updateStart);
    }

    private void recordDiagnostics(long updateStart)
    {
        if (!debugDiagnostics.Value)
            return;

        long now = Stopwatch.GetTimestamp();
        long ticks = now - updateStart;
        diagnosticUpdateTicks += ticks;
        diagnosticMaximumUpdateTicks = Math.Max(diagnosticMaximumUpdateTicks, ticks);
        diagnosticUpdates++;
        TimeSpan elapsed = Stopwatch.GetElapsedTime(diagnosticIntervalStart, now);
        if (elapsed.TotalSeconds < 5)
            return;

        double averageMs = diagnosticUpdates == 0 ? 0 : diagnosticUpdateTicks * 1000.0 / Stopwatch.Frequency / diagnosticUpdates;
        double maximumMs = diagnosticMaximumUpdateTicks * 1000.0 / Stopwatch.Frequency;
        double current = Time.Current;
        double detectedTime = detectedPitchTime.Value;
        double detectedAge = current - detectedTime;
        double newestTime = samples.Count == 0 ? double.NaN : samples[^1].Time;
        double newestAge = current - newestTime;
        double newestDisplayTime = samples.Count == 0 ? double.NaN : samples[^1].DisplayTime;
        double visualAge = current - newestDisplayTime;
        float newestX = samples.Count == 0 ? float.NaN : TimeToX(newestDisplayTime, current, DrawWidth);
        Logger.Log(
            $"Uta debug curve: mode={display.Value} updates={diagnosticUpdates} samples-added={diagnosticSamplesAdded} " +
            $"samples={samples.Count} reference-frames={referenceFrames.Length} " +
            $"reference-segments={referenceSegments.Count} user-segments={userSegments.Count} " +
            $"reference-rebuilds={diagnosticReferenceRebuilds} user-rebuilds={diagnosticUserRebuilds} " +
            $"update-avg={averageMs:0.000}ms update-max={maximumMs:0.000}ms " +
            $"timeline={current:0.0}ms detected-time={detectedTime:0.0}ms detected-age={detectedAge:0.0}ms voice={voiceActive.Value} " +
            $"newest-time={newestTime:0.0}ms newest-age={newestAge:0.0}ms " +
            $"display-time={newestDisplayTime:0.0}ms visual-age={visualAge:0.0}ms " +
            $"newest-x={newestX:0.0}px visual-offset={newestDisplayTime - newestTime:0.0}ms mic-latency={microphoneLatency.Value:0.0}ms " +
            $"reference-x={referenceLayer.X:0.0}px user-x={userLayer.X:0.0}px size={DrawWidth:0.0}x{DrawHeight:0.0} " +
            $"max-timeline-step={diagnosticMaximumPlaybackStep:0.0}ms clears={diagnosticSampleClears} last-clear='{diagnosticLastClearReason}'");
        resetDiagnostics(now);
    }

    private void resetDiagnostics() => resetDiagnostics(Stopwatch.GetTimestamp());

    private void resetDiagnostics(long now)
    {
        diagnosticIntervalStart = now;
        diagnosticUpdateTicks = 0;
        diagnosticMaximumUpdateTicks = 0;
        diagnosticUpdates = 0;
        diagnosticSamplesAdded = 0;
        diagnosticReferenceRebuilds = 0;
        diagnosticUserRebuilds = 0;
        diagnosticSampleClears = 0;
        diagnosticMaximumPlaybackStep = 0;
        diagnosticLastClearReason = "none";
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
                ReferenceFrame previous = referenceFrames[first - 1];
                for (int i = first; i < referenceFrames.Length && previous.Time <= visibleEnd; i++)
                {
                    ReferenceFrame frame = referenceFrames[i];
                    if (frame.Time - previous.Time < sample_interval)
                        continue;

                    if (frame.Time - previous.Time > 140 || Math.Abs(frame.Midi - previous.Midi) > 5.5f)
                    {
                        previous = frame;
                        continue;
                    }

                    if (setSegment(getSegment(referenceSegments, referenceLayer, used), previous.Time, frame.Time,
                                   0, previous.Midi, frame.Midi, reference_colour, 0.30f))
                        used++;
                    previous = frame;
                }
            }
            else
            {
                foreach (UtaNote note in notes)
                {
                    if (note.EndTime < visibleStart || note.StartTime > visibleEnd || note.Midi is not { } midi)
                        continue;

                    if (setSegment(getSegment(referenceSegments, referenceLayer, used), note.StartTime, note.EndTime,
                                   0, midi, midi, reference_colour, 0.30f))
                        used++;
                }
            }
        }

        hideUnused(referenceSegments, used);
    }

    private void drawUser(bool visible)
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
                // Scoring uses the latency-corrected timestamp while rendering uses the time at
                // which the result reached gameplay, matching the immediate trace behaviour of 0.21.
                if (setSegment(getSegment(userSegments, userLayer, used),
                               previous.DisplayTime, current.DisplayTime,
                               0, from, to, colour, alpha))
                    used++;
            }
        }

        hideUnused(userSegments, used);
    }

    private bool setSegment(CurveSegment segment, double fromTime, double toTime, double currentTime,
                            float fromMidi, float toMidi, Color4 colour, float alpha)
    {
        float shiftedCentre = centreMidi + keyShiftSemitones.Value;
        Vector2 start = new(TimeToX(fromTime, currentTime, DrawWidth), MidiToY(fromMidi, shiftedCentre, DrawHeight));
        Vector2 end = new(TimeToX(toTime, currentTime, DrawWidth), MidiToY(toMidi, shiftedCentre, DrawHeight));
        if ((start.Y < 0 && end.Y < 0) || (start.Y > DrawHeight && end.Y > DrawHeight))
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

    internal static float TimeOffsetToX(double geometryTime, double currentTime, float width)
        => (float)((geometryTime - currentTime)
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
            if (manifest == null)
                return Array.Empty<ReferenceFrame>();

            return manifest.IsFormatV2
                ? loadPitchEvidenceFrames(working, manifest)
                : loadLegacyPitchTrackFrames(working, manifest);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            Logger.Log($"Uta could not load the song pitch-analysis curve: {ex.GetBaseException().Message}");
            return Array.Empty<ReferenceFrame>();
        }
    }

    private static ReferenceFrame[] loadLegacyPitchTrackFrames(WorkingBeatmap working, UtzManifest manifest)
    {
        string? pitchStoragePath = manifest.Charts.PitchTrack?.Path is { } path ? working.BeatmapSetInfo.GetPathForFile(path) : null;
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

    /// <summary>
    /// UTZ 0.2 demotes frame-level pitch data to optional evidence stored as a
    /// fixed-hop frequency series rather than the 0.1 pitch track's frame list.
    /// </summary>
    private static ReferenceFrame[] loadPitchEvidenceFrames(WorkingBeatmap working, UtzManifest manifest)
    {
        string? evidenceStoragePath = manifest.Analysis?.PitchEvidence?.Path is { } path ? working.BeatmapSetInfo.GetPathForFile(path) : null;
        if (evidenceStoragePath == null)
            return Array.Empty<ReferenceFrame>();

        using Stream evidenceStream = working.GetStream(evidenceStoragePath);
        UtaPitchEvidence? evidence = JsonSerializer.Deserialize<UtaPitchEvidence>(evidenceStream, UtzPackage.JsonOptions);
        if (evidence == null || evidence.Timebase <= 0 || evidence.Hop <= 0)
            return Array.Empty<ReferenceFrame>();

        var frames = new List<ReferenceFrame>(evidence.FrequencyHz.Count);

        for (int i = 0; i < evidence.FrequencyHz.Count; i++)
        {
            if (evidence.FrequencyHz[i] is not { } hertz || !UtaPitchMath.IsFinitePitch(hertz))
                continue;

            double timeMs = (evidence.Start + (long)i * evidence.Hop) / (double)evidence.Timebase * 1000;
            frames.Add(new ReferenceFrame(timeMs, (float)UtaPitchMath.FrequencyToMidi(hertz)));
        }

        return frames.ToArray();
    }

    private void clearSamples(string reason)
    {
        diagnosticSampleClears++;
        diagnosticLastClearReason = reason;
        if (debugDiagnostics.Value)
        {
            Logger.Log(
                $"Uta debug curve reset: reason='{reason}' timeline={Time.Current:0.0}ms last-playback={lastPlaybackTime:0.0}ms " +
                $"detected-time={detectedPitchTime.Value:0.0}ms samples-before={samples.Count}");
        }

        samples.Clear();
        lastSampleTime = double.NegativeInfinity;
        referenceGeometryTime = double.NaN;
        userGeometryReady = false;
        hideUnused(referenceSegments, 0);
        hideUnused(userSegments, 0);
    }

    protected override void Dispose(bool isDisposing)
    {
        display.UnbindAll();
        detectedPitchMidi.UnbindAll();
        pitchSimilarity.UnbindAll();
        voiceActive.UnbindAll();
        keyShiftSemitones.UnbindAll();
        microphoneLatency.UnbindAll();
        detectedPitchTime.UnbindAll();
        debugDiagnostics.UnbindAll();
        base.Dispose(isDisposing);
    }

    private sealed partial class CurveSegment : Box
    {
        public CurveSegment()
        {
            Anchor = Anchor.TopLeft;
            Origin = Anchor.CentreLeft;
            Height = line_width;
        }
    }

    private readonly record struct CurveSample(double Time, double DisplayTime, float? ReferenceMidi, float? UserMidi, float Similarity);
    internal readonly record struct ReferenceFrame(double Time, float Midi);
}
