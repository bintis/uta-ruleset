// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
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

    private readonly Activity[] activities;
    private IReadOnlyList<SkippableGap> gaps = Array.Empty<SkippableGap>();
    private GameplayClockContainer gameplayClock = null!;
    private DrawableRuleset drawableRuleset = null!;
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
        this.drawableRuleset = drawableRuleset;
        gaps = findSkippableGaps(activities, workingBeatmap.Value.Track.Length);
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
        if (target - gameplayClock.CurrentTime < 50)
            return;

        UtaGameplaySeeker.Seek(gameplayClock, drawableRuleset, action => Schedule(action), target, "gap skip", true);
    }

    internal static IReadOnlyList<SkippableGap> FindSkippableGaps(
        IReadOnlyList<UtaTranscriptSegment> segments,
        IReadOnlyList<UtaPitchNote> notes)
        => findSkippableGaps(activitiesFor(segments, notes), null);

    /// <summary>
    /// Derives practice-navigation phrase boundaries from the same transcript-segment and
    /// target-note activity <see cref="FindSkippableGaps"/> merges - each merged cluster
    /// (rather than the gap between clusters) becomes one phrase.
    /// </summary>
    internal static IReadOnlyList<Phrase> FindPhrases(
        IReadOnlyList<UtaTranscriptSegment> segments,
        IReadOnlyList<UtaPitchNote> notes)
        => phrasesFrom(activitiesFor(segments, notes));

    /// <summary>Gameplay-hit-object overload: <see cref="UtaNote"/> times are already in milliseconds.</summary>
    internal static IReadOnlyList<Phrase> FindPhrases(
        IReadOnlyList<UtaTranscriptSegment> segments,
        IEnumerable<UtaNote> notes)
        => phrasesFrom(
            segments.Select(segment => new Activity(segment.Start * 1000, segment.End * 1000))
                    .Concat(notes.Select(note => new Activity(note.StartTime, note.EndTime))));

    // Phrases merge across gaps up to minimum_gap wide (a short breath between lines still
    // belongs to the same phrase); only a gap that would also qualify as skippable starts a
    // new one. This intentionally differs from the gap=0 merge findSkippableGaps needs, which
    // must not bridge any gap so it can report every skippable one individually.
    private static IReadOnlyList<Phrase> phrasesFrom(IEnumerable<Activity> activities)
        => mergeActivities(activities, minimum_gap)
            .Select(activity => new Phrase(activity.StartTime, activity.EndTime))
            .ToArray();

    private static IEnumerable<Activity> activitiesFor(IReadOnlyList<UtaTranscriptSegment> segments, IReadOnlyList<UtaPitchNote> notes)
        => segments.Select(segment => new Activity(segment.Start * 1000, segment.End * 1000))
                   .Concat(notes.Select(note => new Activity(note.Start * 1000, note.End * 1000)));

    private static IReadOnlyList<SkippableGap> findSkippableGaps(IEnumerable<Activity> source, double? trackEnd)
    {
        List<Activity> merged = mergeActivities(source, 0);
        if (merged.Count == 0)
            return Array.Empty<SkippableGap>();

        var gaps = merged.Zip(merged.Skip(1))
                         .Where(pair => pair.Second.StartTime - pair.First.EndTime >= minimum_gap)
                         .Select(pair => new SkippableGap(pair.First.EndTime, pair.Second.StartTime))
                         .ToList();

        if (trackEnd is { } end && double.IsFinite(end) && end - merged[^1].EndTime >= minimum_gap)
            gaps.Add(new SkippableGap(merged[^1].EndTime, end));

        return gaps;
    }

    private static List<Activity> mergeActivities(IEnumerable<Activity> source, double gapThreshold)
    {
        Activity[] activities = source.Where(activity => activity.EndTime > activity.StartTime)
                                      .OrderBy(activity => activity.StartTime)
                                      .ThenBy(activity => activity.EndTime)
                                      .ToArray();

        if (activities.Length == 0)
            return new List<Activity>();

        var merged = new List<Activity> { activities[0] };
        foreach (Activity activity in activities.Skip(1))
        {
            Activity previous = merged[^1];
            if (activity.StartTime - previous.EndTime <= gapThreshold)
                merged[^1] = previous with { EndTime = Math.Max(previous.EndTime, activity.EndTime) };
            else
                merged.Add(activity);
        }

        return merged;
    }

    private readonly record struct Activity(double StartTime, double EndTime);

    internal readonly record struct SkippableGap(double StartTime, double EndTime);

    /// <summary>A practice-navigable phrase span, in gameplay-clock milliseconds.</summary>
    internal readonly record struct Phrase(double StartTime, double EndTime);
}
