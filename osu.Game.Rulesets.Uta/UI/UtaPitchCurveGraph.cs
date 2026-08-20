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
using osu.Framework.Logging;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Rulesets.Uta.Pitch;
using osu.Game.Rulesets.Uta.Skinning;
using osu.Game.Rulesets.Uta.UI.HUD.Pitch;
using osu.Game.Screens.Play;
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
    // LOOK_BEHIND is 1750ms and samples are at most 50 Hz. Keep a small guard
    // above the visible window; older history is discarded rather than retained.
    private const int buffer_size = 100;
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
    private readonly CurveSample[] samples = new CurveSample[buffer_size];
    private int sampleStart;
    private int sampleCount;
    private readonly List<CurveSegment> referenceSegments = new(384);
    private readonly List<CurveSegment> userSegments = new(buffer_size);
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
    private readonly BindableFloat centreMidi = new();
    private double maximumNoteDuration;
    private double timelineEndTime;
    private double lastSampleTime = double.NegativeInfinity;
    private double lastPlaybackTime = double.NegativeInfinity;
    private double referenceGeometryTime = double.NaN;
    private bool userGeometryReady;
    private bool referenceWasVisible;
    private bool userWasVisible;
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
    private GameplayClockContainer? gameplayClock;
    private UtaPitchStyle style = UtaVisualStyle.Prism().Pitch;
    private Texture? referenceTexture;
    private Texture? liveTexture;

    public UtaPitchCurveGraph()
    {
        RelativeSizeAxes = Axes.Both;

        InternalChildren = new Drawable[]
        {
            referenceLayer = new Container { RelativeSizeAxes = Axes.Y },
            userLayer = new Container { RelativeSizeAxes = Axes.Y },
        };
    }

    public void ApplyStyle(UtaVisualStyle value)
    {
        style = value.Pitch;
        referenceTexture = value.Assets.CurveReference;
        liveTexture = value.Assets.CurveLive;
        foreach (CurveSegment segment in referenceSegments)
            segment.Texture = value.Assets.CurveReference;
        foreach (CurveSegment segment in userSegments)
            segment.Texture = value.Assets.CurveLive;
        referenceGeometryTime = double.NaN;
        userGeometryReady = false;
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap, DrawableRuleset drawableRuleset, UtaRulesetConfigManager config,
                      IBindable<WorkingBeatmap> workingBeatmap, GameplayClockContainer gameplayClock, UtaPitchViewport pitchViewport)
    {
        this.gameplayClock = gameplayClock;
        gameplayClock.OnSeek += onSeek;
        notes = beatmap.HitObjects.OfType<UtaNote>()
                       .Where(note => note.Midi != null)
                       .OrderBy(note => note.StartTime)
                       .ToArray();
        maximumNoteDuration = notes.Length == 0 ? 0 : notes.Max(note => note.Duration);
        centreMidi.BindTo(pitchViewport.CentreMidi);
        timelineEndTime = workingBeatmap.Value.Track.Length;
        if (!double.IsFinite(timelineEndTime))
            timelineEndTime = 0;
        if (notes.Length > 0)
            timelineEndTime = Math.Max(timelineEndTime, notes[^1].EndTime);

        // Evidence is optional visual detail. It must never be allowed to extend or
        // destabilise the authoritative note/playback timeline; unsupported or
        // malformed evidence falls back to the stable chart-note reference line.
        referenceFrames = loadReferenceFrames(workingBeatmap.Value, timelineEndTime);
        timelineEndTime += UtaPitchTimelineGeometry.LOOK_AHEAD;
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
        long updateStart = debugDiagnostics.Value ? Stopwatch.GetTimestamp() : 0;
        base.Update();

        UtaPitchCurveDisplay mode = display.Value;
        if (mode == UtaPitchCurveDisplay.Off)
        {
            if (Alpha != 0)
            {
                Alpha = 0;
                clearSamples("display-off");
            }

            referenceWasVisible = false;
            userWasVisible = false;
            recordDiagnostics(updateStart);
            return;
        }

        Alpha = 1;

        double current = Time.Current;
        double playbackStep = double.IsFinite(lastPlaybackTime) ? current - lastPlaybackTime : 0;
        if (debugDiagnostics.Value)
            diagnosticMaximumPlaybackStep = Math.Max(diagnosticMaximumPlaybackStep, Math.Abs(playbackStep));
        if (double.IsFinite(lastPlaybackTime) && Math.Abs(playbackStep) > 550)
            clearSamples($"timeline-step={playbackStep:+0.0;-0.0;0.0}ms");
        lastPlaybackTime = current;

        bool sampleAdded = false;
        bool active = voiceActive.Value;
        double sampleTime = active ? detectedPitchTime.Value : current;
        bool shouldAddSample = active
            ? sampleTime - lastSampleTime >= sample_interval
            : sampleCount == 0 || sampleAt(sampleCount - 1).UserMidi != null;
        if (shouldAddSample)
        {
            if (!active && double.IsFinite(lastSampleTime))
                sampleTime = Math.Min(current, lastSampleTime + sample_interval);

            UtaNote? target = findNoteAt(sampleTime);
            addSample(new CurveSample(
                sampleTime,
                current,
                target?.Midi,
                active ? detectedPitchMidi.Value : null,
                pitchSimilarity.Value));

            lastSampleTime = sampleTime;
            sampleAdded = true;
            if (debugDiagnostics.Value)
                diagnosticSamplesAdded++;
        }

        // Retain only the sample immediately before the left viewport edge (needed
        // to join the first visible segment) and samples that may be rendered. This
        // is deliberately done to the data buffer, not only to its primitives.
        trimSamplesForViewport(current);

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
                if (debugDiagnostics.Value)
                    diagnosticReferenceRebuilds++;
            }
        }
        else
        {
            referenceLayer.X = 0;
            referenceGeometryTime = double.NaN;
            if (referenceWasVisible)
                drawReference(false, current);
        }

        if (showVoice)
        {
            userLayer.X = TimeOffsetToX(0, current, DrawWidth);
            if (sampleAdded || sizeChanged || !userGeometryReady)
            {
                drawUser(true, current);
                userGeometryReady = true;
                if (debugDiagnostics.Value)
                    diagnosticUserRebuilds++;
            }
        }
        else
        {
            userLayer.X = 0;
            userGeometryReady = false;
            if (userWasVisible)
                drawUser(false, current);
        }

        referenceWasVisible = showSong;
        userWasVisible = showVoice;
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
        double newestTime = sampleCount == 0 ? double.NaN : sampleAt(sampleCount - 1).Time;
        double newestAge = current - newestTime;
        double newestDisplayTime = sampleCount == 0 ? double.NaN : sampleAt(sampleCount - 1).DisplayTime;
        double visualAge = current - newestDisplayTime;
        float newestX = sampleCount == 0 ? float.NaN : TimeToX(newestDisplayTime, current, DrawWidth);
        Logger.Log(
            $"Uta debug curve: mode={display.Value} updates={diagnosticUpdates} samples-added={diagnosticSamplesAdded} " +
            $"samples={sampleCount} reference-frames={referenceFrames.Length} " +
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
        // Keep the optional diagnostic reporters out of phase. When debug logging is
        // enabled they otherwise all write on the same update every five seconds.
        diagnosticIntervalStart = now + Stopwatch.Frequency * 3 / 4;
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
            double visibleStart = currentTime - UtaPitchTimelineGeometry.LOOK_BEHIND;
            double visibleEnd = currentTime + UtaPitchTimelineGeometry.LOOK_AHEAD;

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

                    if (setSegment(getSegment(referenceSegments, referenceLayer, used, styleTexture(reference: true)), previous.Time, frame.Time,
                                   0,
                                   UtaPitchTimelineGeometry.TransposeMidi(previous.Midi, keyShiftSemitones.Value),
                                   UtaPitchTimelineGeometry.TransposeMidi(frame.Midi, keyShiftSemitones.Value),
                                   style.Reference, 0.30f, style.ReferenceCurveWeight))
                        used++;
                    previous = frame;
                }
            }
            else
            {
                int first = lowerBoundNoteStart(visibleStart - maximumNoteDuration);
                int afterLast = upperBoundNoteStart(visibleEnd);
                for (int i = first; i < afterLast; i++)
                {
                    UtaNote note = notes[i];
                    if (note.EndTime < visibleStart || note.Midi is not { } midi)
                        continue;

                    if (setSegment(getSegment(referenceSegments, referenceLayer, used, styleTexture(reference: true)), note.StartTime, note.EndTime,
                                   0,
                                   UtaPitchTimelineGeometry.TransposeMidi(midi, keyShiftSemitones.Value),
                                   UtaPitchTimelineGeometry.TransposeMidi(midi, keyShiftSemitones.Value),
                                   style.Reference, 0.30f, style.ReferenceCurveWeight))
                        used++;
                }
            }
        }

        hideUnused(referenceSegments, used);
    }

    private void drawUser(bool visible, double playbackTime)
    {
        int used = 0;
        if (visible)
        {
            // The buffer has already been trimmed to this viewport plus one boundary
            // sample. Keep this bound here as a defensive guard for a frame that moves
            // the clock after trimming.
            int first = Math.Max(1, lowerBoundSampleDisplayTime(playbackTime - UtaPitchTimelineGeometry.LOOK_BEHIND) - 1);
            for (int i = first; i < sampleCount; i++)
            {
                CurveSample previous = sampleAt(i - 1);
                CurveSample current = sampleAt(i);
                if (previous.DisplayTime > playbackTime + 100)
                    break;
                if (previous.UserMidi is not { } from || current.UserMidi is not { } to)
                    continue;

                (Color4 previousColour, float previousWeight) = userStyle(previous);
                (Color4 currentColour, float currentWeight) = userStyle(current);
                Color4 colour = blend(previousColour, currentColour, 0.5f);
                float alpha = (previousWeight * AgeAlpha(i - 1, sampleCount)
                               + currentWeight * AgeAlpha(i, sampleCount)) / 2;
                if (setSegment(getSegment(userSegments, userLayer, used, styleTexture(reference: false)),
                               previous.DisplayTime, current.DisplayTime,
                               0, from, to, colour, alpha, style.LiveCurveWeight))
                    used++;
            }
        }

        hideUnused(userSegments, used);
    }

    private bool setSegment(CurveSegment segment, double fromTime, double toTime, double currentTime,
                            float fromMidi, float toMidi, Color4 colour, float alpha, float height)
    {
        float shiftedCentre = centreMidi.Value + keyShiftSemitones.Value;
        Vector2 start = new(TimeToX(fromTime, currentTime, DrawWidth), MidiToY(fromMidi, shiftedCentre, DrawHeight));
        Vector2 end = new(TimeToX(toTime, currentTime, DrawWidth), MidiToY(toMidi, shiftedCentre, DrawHeight));
        if ((start.Y < 0 && end.Y < 0) || (start.Y > DrawHeight && end.Y > DrawHeight))
        {
            segment.Alpha = 0;
            return false;
        }

        Vector2 delta = end - start;
        segment.Position = start;
        segment.Width = Math.Max(height, delta.Length);
        segment.Height = height;
        segment.Rotation = MathF.Atan2(delta.Y, delta.X) * 180 / MathF.PI;
        segment.Colour = colour;
        segment.Alpha = alpha;
        return true;
    }

    internal static float MidiToY(float midi, float centre, float height)
        => (centre + UtaPitchTimelineGeometry.VIEW_SPAN / 2 - midi) / UtaPitchTimelineGeometry.VIEW_SPAN * height;

    private void addSample(CurveSample sample)
    {
        if (sampleCount < buffer_size)
        {
            samples[(sampleStart + sampleCount) % buffer_size] = sample;
            sampleCount++;
            return;
        }

        // Fixed-capacity visible-history ring. Replacing the oldest slot preserves the
        // same logical order as List.RemoveAt(0) + Add without shifting structs.
        samples[sampleStart] = sample;
        sampleStart++;
        if (sampleStart == buffer_size)
            sampleStart = 0;
    }

    private CurveSample sampleAt(int logicalIndex)
        => samples[(sampleStart + logicalIndex) % buffer_size];

    private void trimSamplesForViewport(double playbackTime)
    {
        double visibleStart = playbackTime - UtaPitchTimelineGeometry.LOOK_BEHIND;

        // Keep exactly one predecessor for continuity at the left edge. Since samples
        // are chronologically presented, everything before it is neither drawable nor
        // required by any later curve calculation.
        while (sampleCount > 1 && sampleAt(1).DisplayTime < visibleStart)
        {
            sampleStart++;
            if (sampleStart == buffer_size)
                sampleStart = 0;
            sampleCount--;
        }
    }

    private int lowerBoundSampleDisplayTime(double time)
    {
        int low = 0;
        int high = sampleCount;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (sampleAt(middle).DisplayTime < time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private (Color4 Colour, float Weight) userStyle(CurveSample sample)
    {
        if (sample.ReferenceMidi == null)
            return (style.LiveNeutral, 0.55f);

        float similarity = Math.Clamp(sample.Similarity, 0, 1);
        Color4 quality = similarity >= 0.7f
            ? blend(style.LiveNear, style.LiveAccurate, (similarity - 0.7f) / 0.3f)
            : blend(style.LiveOff, style.LiveNear, similarity / 0.7f);
        return (blend(style.LiveNeutral, quality, similarity), 0.35f + similarity * 0.65f);
    }

    internal static float AgeAlpha(int index, int seriesLength)
    {
        float t = index / (float)Math.Max(1, seriesLength - 1);
        return 0.25f + 0.75f * t;
    }

    internal static float TimeToX(double sampleTime, double currentTime, float width)
        => (float)((sampleTime - currentTime + UtaPitchTimelineGeometry.LOOK_BEHIND)
                   / (UtaPitchTimelineGeometry.LOOK_BEHIND + UtaPitchTimelineGeometry.LOOK_AHEAD)) * width;

    internal static float TimeOffsetToX(double geometryTime, double currentTime, float width)
        => (float)((geometryTime - currentTime)
                   / (UtaPitchTimelineGeometry.LOOK_BEHIND + UtaPitchTimelineGeometry.LOOK_AHEAD)) * width;

    private static Color4 blend(Color4 from, Color4 to, float amount) => new(
        from.R + (to.R - from.R) * amount,
        from.G + (to.G - from.G) * amount,
        from.B + (to.B - from.B) * amount,
        1);

    private Texture? styleTexture(bool reference) => reference ? referenceTexture : liveTexture;

    private static CurveSegment getSegment(List<CurveSegment> segments, Container layer, int index, Texture? texture)
    {
        while (segments.Count <= index)
        {
            var segment = new CurveSegment { Texture = texture };
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

    private int lowerBoundNoteStart(double time)
    {
        int low = 0;
        int high = notes.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (notes[middle].StartTime < time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private int upperBoundNoteStart(double time)
    {
        int low = 0;
        int high = notes.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (notes[middle].StartTime <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
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

    private static ReferenceFrame[] loadReferenceFrames(WorkingBeatmap working, double chartEndTime)
    {
        try
        {
            string? manifestPath = working.BeatmapSetInfo.GetPathForFile("manifest.json");
            if (manifestPath == null)
                return Array.Empty<ReferenceFrame>();

            using Stream manifestStream = working.GetStream(manifestPath);
            // Pitch evidence is part of the current uta.song 0.3.x contract. Do not
            // selectively accept analysis from a legacy manifest: its timebase is not a
            // supported playback contract and can make the reference curve jump.
            UtzManifest? manifest = JsonSerializer.Deserialize<UtzManifest>(manifestStream, UtzPackage.JsonOptions);
            if (manifest == null)
                return Array.Empty<ReferenceFrame>();

            ReferenceFrame[] frames = loadPitchEvidenceFrames(working, manifest);
            if (IsStableReferenceEvidence(frames, chartEndTime))
                return frames;

            Logger.Log("Uta ignored unstable pitch evidence and is using the vocal-chart reference curve.");
            return Array.Empty<ReferenceFrame>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            Logger.Log($"Uta could not load the song pitch-analysis curve: {ex.GetBaseException().Message}");
            return Array.Empty<ReferenceFrame>();
        }
    }

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

    internal static bool IsStableReferenceEvidence(ReferenceFrame[] frames, double chartEndTime)
    {
        if (frames.Length < 2 || !double.IsFinite(chartEndTime) || chartEndTime <= 0)
            return false;

        // Evidence timestamps are defined in song time. A full-song frame stream should
        // begin near zero and must not run materially past the chart/audio timeline. This
        // rejects stale converters that used a different clock or unit without guessing a
        // corrective offset that would make the curve visibly drift during gameplay.
        if (frames[0].Time is < -1 or > 1000 || frames[^1].Time > chartEndTime + 1000)
            return false;

        for (int i = 1; i < frames.Length; i++)
        {
            if (!double.IsFinite(frames[i].Time) || !float.IsFinite(frames[i].Midi)
                || frames[i].Time <= frames[i - 1].Time)
                return false;
        }

        return true;
    }

    private void onSeek() => clearSamples("seek");

    private void clearSamples(string reason)
    {
        if (debugDiagnostics.Value)
        {
            diagnosticSampleClears++;
            diagnosticLastClearReason = reason;
        }
        if (debugDiagnostics.Value)
        {
            Logger.Log(
                $"Uta debug curve reset: reason='{reason}' timeline={Time.Current:0.0}ms last-playback={lastPlaybackTime:0.0}ms " +
                $"detected-time={detectedPitchTime.Value:0.0}ms samples-before={sampleCount}");
        }

        sampleStart = 0;
        sampleCount = 0;
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
        centreMidi.UnbindAll();
        if (gameplayClock != null)
            gameplayClock.OnSeek -= onSeek;
        base.Dispose(isDisposing);
    }

    private sealed partial class CurveSegment : UtaTexturedPrimitive
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
