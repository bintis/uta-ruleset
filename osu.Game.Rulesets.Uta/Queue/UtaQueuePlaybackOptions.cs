// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.Uta.Queue;

/// <summary>
/// Per-reservation speed, key and start-time mods. Live control of the playing
/// song is separate; these values are applied when the entry starts.
/// </summary>
public sealed record UtaQueuePlaybackOptions(
    double Speed = 1,
    int Transpose = 0,
    IReadOnlyList<string>? Mods = null)
{
    public const double MinimumSpeed = 0.5;
    public const double MaximumSpeed = 1.5;
    public const int MinimumTranspose = -6;
    public const int MaximumTranspose = 6;

    public static readonly UtaQueuePlaybackOptions Default = new();

    public static readonly HashSet<string> RemoteModAcronyms = new(StringComparer.Ordinal)
    {
        "IQ", "NF", "RX", "VOX", "OCT", "NPG", "NL", "AT", "REC", "PR",
    };

    public IReadOnlyList<string> ModList => Mods ?? Array.Empty<string>();

    public UtaQueuePlaybackOptions Normalized()
    {
        string[] mods = ModList
            .Where(mod => !string.IsNullOrWhiteSpace(mod))
            .Select(mod => mod.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new UtaQueuePlaybackOptions(
            Speed <= 0 ? 1 : Speed,
            Transpose,
            mods);
    }

    public bool TryValidate(out string error)
    {
        if (Speed is < MinimumSpeed or > MaximumSpeed)
        {
            error = "The numeric value is outside the desktop control bounds.";
            return false;
        }

        if (Transpose is < MinimumTranspose or > MaximumTranspose)
        {
            error = "The numeric value is outside the desktop control bounds.";
            return false;
        }

        foreach (string mod in ModList)
        {
            if (string.IsNullOrWhiteSpace(mod) || mod.Length > 16)
            {
                error = "A reservation mod acronym is invalid.";
                return false;
            }

            if (!RemoteModAcronyms.Contains(mod))
            {
                error = "Unknown or unsupported Uta mod.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
