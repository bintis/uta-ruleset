// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Screens.Select.Filter;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Uta.Library;

public sealed record UtaSongLibraryEntry(
    Guid BeatmapId,
    string Title,
    string TitleUnicode,
    string Artist,
    string ArtistUnicode,
    string DifficultyName,
    string Creator,
    long LengthMs,
    DateTimeOffset DateAdded,
    BeatmapInfo Beatmap);

public static class UtaBeatmapEligibility
{
    public static bool CanQueue(BeatmapInfo beatmap)
        => beatmap.Ruleset.ShortName == UtaRuleset.SHORT_NAME && !beatmap.Hidden && !beatmap.BeatmapSet!.DeletePending;
}

public sealed partial class UtaSongLibrary : Component
{
    private IReadOnlyList<UtaSongLibraryEntry> entries = Array.Empty<UtaSongLibraryEntry>();
    private readonly Bindable<SortMode> currentSort = new();

    [BackgroundDependencyLoader]
    private void load(BeatmapManager beatmapManager, OsuConfigManager config)
    {
        OsuSetting sortingSetting = Enum.Parse<OsuSetting>(nameof(OsuSetting.SongSelectSortingMode));
        config.BindWith(sortingSetting, currentSort);
        entries = beatmapManager.GetAllUsableBeatmapSets()
                                .SelectMany(set => set.Beatmaps)
                                .Where(UtaBeatmapEligibility.CanQueue)
                                .Select(beatmap => new UtaSongLibraryEntry(
                                    beatmap.ID,
                                    beatmap.Metadata.Title,
                                    beatmap.Metadata.TitleUnicode,
                                    beatmap.Metadata.Artist,
                                    beatmap.Metadata.ArtistUnicode,
                                    beatmap.DifficultyName,
                                    beatmap.Metadata.Author.Username,
                                    (long)beatmap.Length,
                                    beatmap.BeatmapSet!.DateAdded,
                                    beatmap))
                                .ToArray();
    }

    public UtaSongLibraryEntry? Find(Guid beatmapId) => entries.FirstOrDefault(entry => entry.BeatmapId == beatmapId);

    public IReadOnlyList<UtaSongLibraryEntry> Browse(string? query)
        => order(filter(query)).ToArray();

    public const int RemotePageSize = 80;

    public IReadOnlyList<UtaSongLibraryEntry> Search(string? query, int offset = 0, int maximum = RemotePageSize)
    {
        int start = Math.Max(0, offset);
        int take = Math.Clamp(maximum, 1, RemotePageSize);
        return order(filter(query)).Skip(start).Take(take).ToArray();
    }

    private IEnumerable<UtaSongLibraryEntry> filter(string? query)
    {
        IEnumerable<UtaSongLibraryEntry> result = entries;
        if (!string.IsNullOrWhiteSpace(query))
        {
            string term = query.Trim();
            result = result.Where(entry =>
                entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || entry.TitleUnicode.Contains(term, StringComparison.OrdinalIgnoreCase)
                || entry.Artist.Contains(term, StringComparison.OrdinalIgnoreCase)
                || entry.ArtistUnicode.Contains(term, StringComparison.OrdinalIgnoreCase)
                || entry.DifficultyName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || entry.Creator.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    private IOrderedEnumerable<UtaSongLibraryEntry> order(IEnumerable<UtaSongLibraryEntry> source)
    {
        IOrderedEnumerable<UtaSongLibraryEntry> ordered = currentSort.Value switch
        {
            SortMode.Artist => source.OrderBy(entry => entry.Beatmap.BeatmapSet!.Metadata.Artist, OrdinalSortByCaseStringComparer.DEFAULT),
            SortMode.Author => source.OrderBy(entry => entry.Beatmap.BeatmapSet!.Metadata.Author.Username, OrdinalSortByCaseStringComparer.DEFAULT),
            SortMode.BPM => source.OrderBy(entry => entry.Beatmap.BPM),
            SortMode.DateAdded => source.OrderByDescending(entry => entry.Beatmap.BeatmapSet!.DateAdded),
            SortMode.DateRanked => source.OrderByDescending(entry => entry.Beatmap.BeatmapSet!.DateRanked),
            SortMode.DateSubmitted => source.OrderByDescending(entry => entry.Beatmap.BeatmapSet!.DateSubmitted),
            SortMode.Difficulty => source.OrderBy(entry => entry.Beatmap.StarRating),
            SortMode.LastPlayed => source.OrderByDescending(entry => entry.Beatmap.LastPlayed),
            SortMode.Length => source.OrderBy(entry => entry.Beatmap.Length),
            SortMode.Source => source.OrderBy(entry => entry.Beatmap.BeatmapSet!.Metadata.Source, OrdinalSortByCaseStringComparer.DEFAULT),
            _ => source.OrderBy(entry => entry.Beatmap.BeatmapSet!.Metadata.Title, OrdinalSortByCaseStringComparer.DEFAULT),
        };

        return ordered.ThenBy(entry => entry.Beatmap.BeatmapSet!.Metadata.Title, OrdinalSortByCaseStringComparer.DEFAULT)
                      .ThenByDescending(entry => entry.Beatmap.BeatmapSet!.DateAdded)
                      .ThenByDescending(entry => entry.Beatmap.BeatmapSet!.ID);
    }
}
