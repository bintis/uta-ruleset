// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Text;

namespace osu.Game.Rulesets.Karaoke.Integration.Uta;

/// <summary>
/// Persists a UTZ musical key through lazer's beatmap metadata database.
/// </summary>
public static class UtaKeySignature
{
    private const string tag_prefix = "uta-key:";

    public static string? CreateMetadataTag(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(key.Trim()))
                                .TrimEnd('=')
                                .Replace('+', '-')
                                .Replace('/', '_');
        return tag_prefix + encoded;
    }

    public static string? FromMetadataTags(string? tags)
    {
        string? encoded = tags?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                              .FirstOrDefault(tag => tag.StartsWith(tag_prefix, StringComparison.Ordinal));
        if (encoded == null)
            return null;

        encoded = encoded[tag_prefix.Length..].Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
