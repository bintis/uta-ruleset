// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace osu.Game.Rulesets.Uta.Performance;

internal static class UtaPerformanceJson
{
    public static JsonSerializerOptions Options { get; } = create(true);
    public static JsonSerializerOptions CompactOptions { get; } = create(false);

    private static JsonSerializerOptions create(bool indented)
        => new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = indented,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        };
}
