// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace osu.Game.Rulesets.Uta.Import;

public sealed record UtaImportDiagnostic(
    DateTimeOffset Timestamp,
    string FileName,
    string Category,
    string Message);

/// <summary>
/// Bounded user-facing import history. Full exceptions still go to the normal
/// lazer log, while this view strips paths, line breaks and stack information.
/// </summary>
public static class UtaImportDiagnostics
{
    private const int capacity = 32;
    private static readonly object sync = new();
    private static readonly Queue<UtaImportDiagnostic> entries = new(capacity);

    public static UtaImportDiagnostic Record(string path, Exception exception)
    {
        Exception root = exception.GetBaseException();
        string fileName = string.IsNullOrWhiteSpace(path) ? "unnamed.utz" : Path.GetFileName(path);
        string message = sanitise(root.Message, path);
        var entry = new UtaImportDiagnostic(DateTimeOffset.Now, fileName, classify(root), message);

        lock (sync)
        {
            while (entries.Count >= capacity)
                entries.Dequeue();
            entries.Enqueue(entry);
        }

        return entry;
    }

    public static IReadOnlyList<UtaImportDiagnostic> Snapshot()
    {
        lock (sync)
            return entries.Reverse().ToArray();
    }

    public static void Clear()
    {
        lock (sync)
            entries.Clear();
    }

    private static string classify(Exception exception)
        => exception switch
        {
            JsonException => "Invalid manifest/chart JSON",
            InvalidDataException => "Invalid or unsafe package",
            NotSupportedException => "Unsupported package version or feature",
            UnauthorizedAccessException => "File access denied",
            IOException => "Package could not be read",
            _ => "Import failed",
        };

    private static string sanitise(string value, string path)
    {
        string result = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (!string.IsNullOrWhiteSpace(path))
            result = result.Replace(path, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);

        // Avoid accidental path disclosure from nested parsers without hiding the useful reason.
        foreach (string token in result.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Contains('/') || token.Contains('\\'))
            {
                string leaf = token.Replace('\\', '/').Split('/')[^1];
                result = result.Replace(token, leaf, StringComparison.Ordinal);
            }
        }

        if (string.IsNullOrWhiteSpace(result))
            result = "The package did not satisfy the uta! import contract.";
        return result.Length <= 240 ? result : result[..237] + "...";
    }
}
