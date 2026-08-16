// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;

namespace osu.Game.Rulesets.Uta.Performance;

public sealed class UtaPerformancePaths
{
    public string RootDirectory { get; }
    public string PerformancesDirectory => Path.Combine(RootDirectory, "performances");
    public string IndexPath => Path.Combine(RootDirectory, "index-v1.json");

    public UtaPerformancePaths(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("A performance root directory is required.", nameof(rootDirectory));

        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string GetPerformanceDirectory(Guid performanceId)
    {
        if (performanceId == Guid.Empty)
            throw new ArgumentException("A non-empty performance ID is required.", nameof(performanceId));
        return Path.Combine(PerformancesDirectory, performanceId.ToString("D"));
    }

    public static string ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || Path.IsPathRooted(fileName)
            || fileName is "." or ".."
            || fileName.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0
            || fileName != Path.GetFileName(fileName))
            throw new ArgumentException("Performance asset names must be simple portable file names.", nameof(fileName));

        return fileName;
    }

    public static string ResolveContainedFile(string directory, string relativeFileName)
    {
        ValidateFileName(relativeFileName);
        string root = Path.GetFullPath(directory);
        string candidate = Path.GetFullPath(Path.Combine(root, relativeFileName));
        string relative = Path.GetRelativePath(root, candidate);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("A performance asset escaped its containing directory.");
        return candidate;
    }
}
