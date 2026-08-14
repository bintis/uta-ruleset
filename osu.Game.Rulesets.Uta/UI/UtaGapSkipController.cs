// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Offers lazer's native skip action during long gaps between vocal activity,
/// and during a trailing silent section. The native player owns intro skipping.
/// </summary>
internal sealed partial class UtaGapSkipController : CompositeDrawable
{
    private const double minimum_gap = 3000;

    private static readonly PropertyInfo? frame_stable_playback = typeof(DrawableRuleset).GetProperty(
        "FrameStablePlayback", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly Activity[] activities;
    private IReadOnlyList<SkippableGap> gaps = Array.Empty<SkippableGap>();
    private GameplayClockContainer gameplayClock = null!;
    private SkipOverlay? activeOverlay;
    private int gapIndex;

    public UtaGapSkipController(UtaBeatmap beatmap)
    {
        RelativeSizeAxes = Axes.Both;
        activities = beatmap.HitObjects.OfType<UtaNote>()
                            .Select(note => new Activity(note.StartTime, note.EndTime))
                            .Concat(beatmap.Transcript.Select(segment => new Activity(segment.Start * 1000, segment.End * 1000)))
                            .ToArray();
    }

    [BackgroundDependencyLoader]
    private void load(GameplayClockContainer clock, DrawableRuleset drawableRuleset, IBindable<WorkingBeatmap> workingBeatmap)
    {
        gameplayClock = clock;
        gaps = findSkippableGaps(activities, workingBeatmap.Value.Track.Length);

        // Uta has no replay or score judgements which need intermediate frames.
        // Keeping frame stability enabled after a gap seek can leave the ruleset
        // frozen forever when lazer rejects a later transient BASS clock jump.
        if (frame_stable_playback == null)
            Logger.Log("Uta could not disable lazer's frame-stable playback.", level: LogLevel.Error);
        else
            frame_stable_playback.SetValue(drawableRuleset, false);
    }

    protected override void Update()
    {
        base.Update();

        double current = gameplayClock.CurrentTime;
        while (gapIndex < gaps.Count && current >= gaps[gapIndex].EndTime - MasterGameplayClockContainer.MINIMUM_SKIP_TIME)
        {
            activeOverlay?.Expire();
            activeOverlay = null;
            gapIndex++;
        }

        if (activeOverlay != null || gapIndex >= gaps.Count)
            return;

        SkippableGap gap = gaps[gapIndex];
        if (current < gap.StartTime || current >= gap.EndTime - MasterGameplayClockContainer.MINIMUM_SKIP_TIME)
            return;

        AddInternal(activeOverlay = new SkipOverlay(gap.EndTime)
        {
            RequestSkip = () => performImmediateSeek(gap.EndTime - MasterGameplayClockContainer.MINIMUM_SKIP_TIME),
        });
    }

    private void performImmediateSeek(double target)
    {
        double current = gameplayClock.CurrentTime;
        if (gameplayClock.IsPaused.Value || !gameplayClock.IsRunning || target - current < 50)
            return;

        try
        {
            gameplayClock.Seek(target);
            Logger.Log($"Uta gap skip: {current:N0} ms -> {target:N0} ms.");
        }
        catch (Exception ex)
        {
            Logger.Log($"Uta gap skip failed: {ex.GetBaseException().Message}", level: LogLevel.Error);
        }
    }

    internal static IReadOnlyList<SkippableGap> FindSkippableGaps(
        IReadOnlyList<UtaTranscriptSegment> segments,
        IReadOnlyList<UtaPitchNote> notes)
        => findSkippableGaps(
            segments.Select(segment => new Activity(segment.Start * 1000, segment.End * 1000))
                    .Concat(notes.Select(note => new Activity(note.Start * 1000, note.End * 1000))), null);

    private static IReadOnlyList<SkippableGap> findSkippableGaps(IEnumerable<Activity> source, double? trackEnd)
    {
        Activity[] activities = source.Where(activity => activity.EndTime > activity.StartTime)
                                      .OrderBy(activity => activity.StartTime)
                                      .ThenBy(activity => activity.EndTime)
                                      .ToArray();

        if (activities.Length == 0)
            return Array.Empty<SkippableGap>();

        var merged = new List<Activity> { activities[0] };
        foreach (Activity activity in activities.Skip(1))
        {
            Activity previous = merged[^1];
            if (activity.StartTime <= previous.EndTime)
                merged[^1] = previous with { EndTime = Math.Max(previous.EndTime, activity.EndTime) };
            else
                merged.Add(activity);
        }

        var gaps = merged.Zip(merged.Skip(1))
                         .Where(pair => pair.Second.StartTime - pair.First.EndTime >= minimum_gap)
                         .Select(pair => new SkippableGap(pair.First.EndTime, pair.Second.StartTime))
                         .ToList();

        if (trackEnd is { } end && double.IsFinite(end) && end - merged[^1].EndTime >= minimum_gap)
            gaps.Add(new SkippableGap(merged[^1].EndTime, end));

        return gaps;
    }

    private readonly record struct Activity(double StartTime, double EndTime);

    internal readonly record struct SkippableGap(double StartTime, double EndTime);
}
