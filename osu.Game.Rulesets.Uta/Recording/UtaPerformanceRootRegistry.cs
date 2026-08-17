// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;

namespace osu.Game.Rulesets.Uta.Recording;

internal static class UtaPerformanceRootRegistry
{
    private static readonly object sync = new();
    private static string configuredRoot = string.Empty;

    public static void SetConfiguredRoot(string? path)
    {
        lock (sync)
            configuredRoot = path?.Trim() ?? string.Empty;
    }

    public static string Resolve()
    {
        lock (sync)
        {
            if (!string.IsNullOrWhiteSpace(configuredRoot))
                return Path.GetFullPath(configuredRoot);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "osu",
            "uta-performances");
    }
}
