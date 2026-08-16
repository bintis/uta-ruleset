// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Performance;

/// <summary>
/// Atomically adds the final lazer ScoreInfo identity after native score import.
/// The performance ID remains the archive's primary identity.
/// </summary>
public sealed class UtaPerformanceScoreLinker
{
    private readonly UtaPerformanceArchiveReader reader = new();

    public async Task<UtaPerformanceManifest> LinkAsync(
        string performanceDirectory,
        Guid lazerScoreId,
        string? lazerScoreHash,
        CancellationToken cancellationToken = default)
    {
        if (lazerScoreId == Guid.Empty)
            throw new ArgumentException("A non-empty lazer score ID is required.", nameof(lazerScoreId));

        string directory = Path.GetFullPath(performanceDirectory);
        UtaPerformanceManifest manifest = await reader.ReadManifestAsync(directory, true, cancellationToken).ConfigureAwait(false);
        manifest.LazerScoreId = lazerScoreId;
        manifest.LazerScoreHash = string.IsNullOrWhiteSpace(lazerScoreHash) ? null : lazerScoreHash;

        string temporary = UtaPerformancePaths.ResolveContainedFile(directory, UtaPerformanceArchiveWriter.MANIFEST_FILENAME + $".tmp-{Guid.NewGuid():N}");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(output, manifest, UtaPerformanceJson.Options, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            string manifestPath = UtaPerformancePaths.ResolveContainedFile(directory, UtaPerformanceArchiveWriter.MANIFEST_FILENAME);
            File.Move(temporary, manifestPath, true);

            DirectoryInfo? performancesDirectory = Directory.GetParent(directory);
            DirectoryInfo? rootDirectory = performancesDirectory?.Parent;
            if (rootDirectory != null)
            {
                try
                {
                    await UtaPerformanceIndexStore.RebuildAsync(rootDirectory.FullName, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The index is a rebuildable cache; the manifest link is authoritative.
                }
            }

            return manifest;
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
}
