// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;

namespace osu.Game.Rulesets.Uta.Recording;

public sealed class UtaRecordingStorageService
{
    private readonly string root;

    public UtaRecordingStorageService(string rootDirectory)
    {
        root = Path.GetFullPath(rootDirectory);
    }

    public long GetTotalBytes()
    {
        if (!Directory.Exists(root))
            return 0;

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                        .Sum(path =>
                        {
                            try
                            {
                                return new FileInfo(path).Length;
                            }
                            catch
                            {
                                return 0;
                            }
                        });
    }

    public int CleanupStaging(TimeSpan minimumAge)
    {
        string staging = Path.Combine(root, "staging");
        if (!Directory.Exists(staging))
            return 0;

        DateTime threshold = DateTime.UtcNow - minimumAge;
        int removed = 0;
        foreach (string directory in Directory.EnumerateDirectories(staging))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) > threshold)
                    continue;

                Directory.Delete(directory, true);
                removed++;
            }
            catch
            {
            }
        }

        return removed;
    }

    public bool DeletePerformance(Guid performanceId)
    {
        if (performanceId == Guid.Empty)
            throw new ArgumentException("Performance ID is required.", nameof(performanceId));

        string directory = Path.Combine(root, "performances", performanceId.ToString("D"));
        if (!Directory.Exists(directory))
            return false;

        Directory.Delete(directory, true);
        return true;
    }
}
