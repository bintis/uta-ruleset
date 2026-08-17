// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.Uta.Performance;

/// <summary>
/// Resolves the user-visible performance archive root. Resolution order is the
/// UTA_PERFORMANCE_ROOT environment variable, the remembered pointer file, then
/// osu!'s exports/uta-performances directory.
/// </summary>
public static class UtaPerformanceRootResolver
{
    public const string ENVIRONMENT_VARIABLE = "UTA_PERFORMANCE_ROOT";
    public const string DEFAULT_RELATIVE_ROOT = "exports/uta-performances";
    public const string POINTER_RELATIVE_PATH = "exports/uta-performance-root.txt";

    public static string ResolveAndRemember(GameHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        string root = Resolve(host);
        remember(host, root);
        return root;
    }

    public static string Resolve(GameHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        string? configured = Environment.GetEnvironmentVariable(ENVIRONMENT_VARIABLE);
        if (string.IsNullOrWhiteSpace(configured))
        {
            string pointerPath = host.Storage.GetFullPath(POINTER_RELATIVE_PATH);
            try
            {
                if (File.Exists(pointerPath))
                    configured = File.ReadAllText(pointerPath).Trim();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return resolveCandidate(host, configured);
    }

    private static string resolveCandidate(GameHost host, string? configured)
    {
        string candidate = Environment.ExpandEnvironmentVariables(configured?.Trim().Trim('"') ?? string.Empty);
        if (candidate.StartsWith("~" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || candidate.StartsWith("~" + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                candidate[2..]);
        }

        string fullPath;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            fullPath = host.Storage.GetFullPath(DEFAULT_RELATIVE_ROOT + Path.DirectorySeparatorChar, true);
        }
        else if (Path.IsPathRooted(candidate))
        {
            fullPath = Path.GetFullPath(candidate);
            Directory.CreateDirectory(fullPath);
        }
        else
        {
            fullPath = host.Storage.GetFullPath(candidate + Path.DirectorySeparatorChar, true);
        }

        Directory.CreateDirectory(fullPath);
        return Path.GetFullPath(fullPath);
    }

    private static void remember(GameHost host, string root)
    {
        try
        {
            host.Storage.GetFullPath("exports" + Path.DirectorySeparatorChar, true);
            using var writer = new StreamWriter(host.Storage.CreateFileSafely(POINTER_RELATIVE_PATH));
            writer.WriteLine(root);
        }
        catch (IOException)
        {
            // The archive itself may still be writable even when osu!'s pointer
            // file cannot be updated. Results will use the default root later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
