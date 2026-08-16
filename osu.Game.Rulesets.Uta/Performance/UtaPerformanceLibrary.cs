// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Performance;

public sealed class UtaPerformanceLibrary
{
    private readonly UtaPerformancePaths paths;
    private readonly UtaPerformanceArchiveReader reader = new();
    private readonly object sync = new();
    private Dictionary<Guid, UtaPerformanceArchiveEntry> byPerformanceId = new();
    private Dictionary<Guid, UtaPerformanceArchiveEntry> byLazerScoreId = new();

    public IReadOnlyCollection<UtaPerformanceArchiveEntry> Entries
    {
        get
        {
            lock (sync)
                return byPerformanceId.Values.ToArray();
        }
    }

    public UtaPerformanceLibrary(string rootDirectory)
    {
        paths = new UtaPerformancePaths(rootDirectory);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var refreshedByPerformanceId = new Dictionary<Guid, UtaPerformanceArchiveEntry>();
        var refreshedByLazerScoreId = new Dictionary<Guid, UtaPerformanceArchiveEntry>();
        Directory.CreateDirectory(paths.PerformancesDirectory);
        UtaPerformanceRecovery.RemoveStalePartialDirectories(paths.RootDirectory, TimeSpan.FromDays(1));

        foreach (string directory in Directory.EnumerateDirectories(paths.PerformancesDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetFileName(directory).StartsWith(".partial-", StringComparison.Ordinal))
                continue;
            if (!File.Exists(Path.Combine(directory, UtaPerformanceArchiveWriter.COMPLETE_FILENAME)))
                continue;

            try
            {
                UtaPerformanceManifest manifest = await reader.ReadManifestAsync(directory, false, cancellationToken).ConfigureAwait(false);
                var entry = new UtaPerformanceArchiveEntry(directory, manifest, true);
                if (!refreshedByPerformanceId.TryGetValue(manifest.PerformanceId, out UtaPerformanceArchiveEntry? existing)
                    || entry.Manifest.CreatedAtUtc >= existing.Manifest.CreatedAtUtc)
                    refreshedByPerformanceId[manifest.PerformanceId] = entry;
                if (manifest.LazerScoreId is { } scoreId
                    && (!refreshedByLazerScoreId.TryGetValue(scoreId, out UtaPerformanceArchiveEntry? existingScore)
                        || entry.Manifest.CreatedAtUtc >= existingScore.Manifest.CreatedAtUtc))
                    refreshedByLazerScoreId[scoreId] = entry;
            }
            catch (InvalidDataException)
            {
                // Invalid archives remain on disk for diagnostics but are not indexed.
            }
            catch (IOException)
            {
                // The storage device may have become unavailable mid-scan.
            }
            catch (UnauthorizedAccessException)
            {
                // The configured archive root may have lost permissions.
            }
        }

        lock (sync)
        {
            byPerformanceId = refreshedByPerformanceId;
            byLazerScoreId = refreshedByLazerScoreId;
        }
    }

    public UtaPerformanceArchiveEntry? FindByPerformanceId(Guid performanceId)
    {
        lock (sync)
            return byPerformanceId.GetValueOrDefault(performanceId);
    }

    public UtaPerformanceArchiveEntry? FindByLazerScoreId(Guid scoreId)
    {
        lock (sync)
            return byLazerScoreId.GetValueOrDefault(scoreId);
    }

    public IReadOnlyList<UtaPerformanceArchiveEntry> FindByPackageId(string packageId)
    {
        lock (sync)
        {
            return byPerformanceId.Values.Where(entry => string.Equals(entry.Manifest.Song.PackageId, packageId, StringComparison.Ordinal))
                                  .OrderByDescending(entry => entry.Manifest.CreatedAtUtc)
                                  .ToArray();
        }
    }
}

public static class UtaPerformanceIndexStore
{
    private static readonly SemaphoreSlim write_gate = new(1, 1);

    public static async Task RebuildAsync(string rootDirectory, CancellationToken cancellationToken = default)
    {
        var paths = new UtaPerformancePaths(rootDirectory);
        var library = new UtaPerformanceLibrary(rootDirectory);
        await library.RefreshAsync(cancellationToken).ConfigureAwait(false);

        var index = new UtaPerformanceIndex
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Items = library.Entries.OrderByDescending(entry => entry.Manifest.CreatedAtUtc)
                           .Select(entry => new UtaPerformanceIndexItem(
                               entry.Manifest.PerformanceId,
                               entry.Manifest.LazerScoreId,
                               entry.Manifest.CreatedAtUtc,
                               entry.Manifest.Song.PackageId,
                               entry.Manifest.Song.BeatmapHash,
                               entry.Manifest.Scoring.TotalScore,
                               Path.GetRelativePath(paths.RootDirectory, entry.DirectoryPath).Replace(Path.DirectorySeparatorChar, '/')))
                           .ToArray(),
        };

        await write_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(paths.RootDirectory);
            string temporary = paths.IndexPath + $".tmp-{Guid.NewGuid():N}";
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await JsonSerializer.SerializeAsync(output, index, UtaPerformanceJson.Options, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                File.Move(temporary, paths.IndexPath, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                catch
                {
                }
            }
        }
        finally
        {
            write_gate.Release();
        }
    }
}

public static class UtaPerformanceRecovery
{
    public static int RemoveStalePartialDirectories(string rootDirectory, TimeSpan minimumAge)
    {
        var paths = new UtaPerformancePaths(rootDirectory);
        if (!Directory.Exists(paths.PerformancesDirectory))
            return 0;

        int removed = 0;
        DateTime threshold = DateTime.UtcNow - minimumAge;
        foreach (string directory in Directory.EnumerateDirectories(paths.PerformancesDirectory, ".partial-*"))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) > threshold)
                    continue;

                Directory.Delete(directory, true);
                removed++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }
}
