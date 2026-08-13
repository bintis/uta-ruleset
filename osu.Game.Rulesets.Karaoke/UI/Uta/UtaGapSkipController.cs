// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Karaoke.Beatmaps;
using osu.Game.Rulesets.Karaoke.Integration.Uta;
using osu.Game.Rulesets.Karaoke.Objects;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Karaoke.UI.Uta;

/// <summary>
/// Reuses lazer's native <see cref="SkipOverlay"/> for long silent periods between
/// karaoke phrases. Intro and outro skips remain owned by the native player.
/// </summary>
public partial class UtaGapSkipController : CompositeDrawable
{
    private const double minimum_gap = 3000;

    private readonly Activity[] activities;
    private IReadOnlyList<SkippableGap> gaps = Array.Empty<SkippableGap>();
    private GameplayClockContainer gameplayClock = null!;
    private SkipOverlay? activeOverlay;
    private int gapIndex;

    public UtaGapSkipController(KaraokeBeatmap beatmap)
    {
        RelativeSizeAxes = Axes.Both;

        activities = beatmap.HitObjects.OfType<Note>()
                            .Where(note => note.Display)
                            .Select(note => new Activity(note.StartTime, note.EndTime))
                            .Concat(beatmap.UtaTranscriptSegments.Select(segment => new Activity(segment.Start * 1000, segment.End * 1000)))
                            .ToArray();
    }

    [BackgroundDependencyLoader]
    private void load(GameplayClockContainer clock, IBindable<WorkingBeatmap> workingBeatmap)
    {
        gameplayClock = clock;
        double trackEnd = workingBeatmap.Value.Track.Length;
        gaps = findSkippableGaps(activities, trackEnd);
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
            RequestSkip = () => gameplayClock.Seek(gap.EndTime - MasterGameplayClockContainer.MINIMUM_SKIP_TIME),
        });
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

        if (activities.Length < 2)
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
