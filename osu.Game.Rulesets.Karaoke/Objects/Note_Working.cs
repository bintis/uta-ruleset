// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Rulesets.Karaoke.Beatmaps;
using osu.Game.Rulesets.Karaoke.Beatmaps.Metadatas;
using osu.Game.Rulesets.Karaoke.Objects.Types;
using osu.Game.Rulesets.Karaoke.Objects.Workings;

namespace osu.Game.Rulesets.Karaoke.Objects;

/// <summary>
/// Placing the properties that set by <see cref="KaraokeBeatmapProcessor"/> or being calculated.
/// Those properties will not be saved into the beatmap.
/// </summary>
public partial class Note : IHasWorkingProperty<NoteWorkingProperty>
{
    [JsonIgnore]
    private readonly NoteWorkingPropertyValidator workingPropertyValidator;

    public bool InvalidateWorkingProperty(NoteWorkingProperty workingProperty)
        => workingPropertyValidator.Invalidate(workingProperty);

    private void updateStateByWorkingProperty(NoteWorkingProperty workingProperty)
        => workingPropertyValidator.UpdateStateByWorkingProperty(workingProperty);

    public NoteWorkingProperty[] GetAllInvalidWorkingProperties()
        => workingPropertyValidator.GetAllInvalidFlags();

    public void ValidateWorkingProperty(KaraokeBeatmap beatmap)
    {
        foreach (var flag in GetAllInvalidWorkingProperties())
        {
            switch (flag)
            {
                case NoteWorkingProperty.Page:
                    PageIndex = getPageIndex(beatmap, StartTime);
                    break;

                case NoteWorkingProperty.ReferenceLyric:
                    ReferenceLyric = findLyricById(beatmap, ReferenceLyricId);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        static int? getPageIndex(KaraokeBeatmap beatmap, double startTime)
            => beatmap.PageInfo.GetPageIndexAt(startTime);

        static Lyric? findLyricById(IBeatmap beatmap, ElementId? id) =>
            id == null ? null : beatmap.HitObjects.OfType<Lyric>().Single(x => x.ID == id);
    }

    [JsonIgnore]
    public readonly Bindable<int?> PageIndexBindable = new();

    /// <summary>
    /// Order
    /// </summary>
    [JsonIgnore]
    public int? PageIndex
    {
        get => PageIndexBindable.Value;
        set
        {
            PageIndexBindable.Value = value;
            updateStateByWorkingProperty(NoteWorkingProperty.Page);
        }
    }

    private double? authoredStartTime;

    /// <summary>
    /// Exact authored start time for package-native notes such as UTZ charts.
    /// When null, legacy notes continue to derive timing from lyric time tags.
    /// </summary>
    public double? AuthoredStartTime
    {
        get => authoredStartTime;
        set
        {
            authoredStartTime = value;
            syncStartTimeAndDurationFromTimeTag();
        }
    }

    public int? Midi { get; set; }

    public string? NoteKind { get; set; }

    public bool UtaOctaveTolerance { get; set; }

    [JsonIgnore]
    public override double StartTime
    {
        get => AuthoredStartTime ?? base.StartTime;
        set
        {
            AuthoredStartTime = value;
            base.StartTime = value;
        }
    }

    [JsonIgnore]
    public readonly Bindable<double> DurationBindable = new BindableDouble();

    private double? authoredDuration;

    /// <summary>
    /// Exact authored duration for package-native notes.
    /// </summary>
    public double? AuthoredDuration
    {
        get => authoredDuration;
        set
        {
            authoredDuration = value;
            syncStartTimeAndDurationFromTimeTag();
        }
    }

    [JsonIgnore]
    public double Duration
    {
        get => AuthoredDuration ?? DurationBindable.Value;
        set
        {
            AuthoredDuration = value;
            DurationBindable.Value = value;
        }
    }

    /// <summary>
    /// End time.
    /// There's no need to save the time because it's calculated by the <see cref="TimeTag"/>
    /// </summary>
    [JsonIgnore]
    public double EndTime => StartTime + Duration;

    [JsonIgnore]
    public readonly Bindable<Lyric?> ReferenceLyricBindable = new();

    [JsonIgnore]
    public readonly BindableDictionary<Singer, SingerState[]> SingersBindable = new();

    /// <summary>
    /// Singers
    /// </summary>
    [JsonIgnore]
    public IDictionary<Singer, SingerState[]> Singers
    {
        get => SingersBindable;
        set
        {
            SingersBindable.Clear();
            SingersBindable.AddRange(value);
        }
    }

    /// <summary>
    /// Relative lyric.
    /// Technically parent lyric will not change after assign, but should not restrict in model layer.
    /// </summary>
    [JsonIgnore]
    public Lyric? ReferenceLyric
    {
        get => ReferenceLyricBindable.Value;
        set
        {
            ReferenceLyricBindable.Value = value;
            updateStateByWorkingProperty(NoteWorkingProperty.ReferenceLyric);
        }
    }

    public TimeTag? StartReferenceTimeTag => ReferenceLyric?.TimeTags.ElementAtOrDefault(ReferenceTimeTagIndex);

    public TimeTag? EndReferenceTimeTag => ReferenceLyric?.TimeTags.ElementAtOrDefault(ReferenceTimeTagIndex + 1);
}
