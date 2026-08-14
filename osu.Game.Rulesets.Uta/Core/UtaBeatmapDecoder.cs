// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Rulesets.Uta.Formats;

namespace osu.Game.Rulesets.Uta.Core;

/// <summary>
/// Minimal decoder for the private beatmap payload emitted by <see cref="UtzBeatmapSetConverter"/>.
/// It registers only the Uta header and never replaces lazer's standard fallback decoder.
/// </summary>
public sealed class UtaBeatmapDecoder : LegacyBeatmapDecoder
{
    public new const int LATEST_VERSION = 1;

    private readonly List<string> notes = new();
    private string? metadata;
    private static int registered;

    public new static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0)
            return;

        AddDecoder<Beatmap>("uta file format v", marker => new UtaBeatmapDecoder(Parsing.ParseInt(marker.Split('v').Last())));
    }

    public UtaBeatmapDecoder(int version = LATEST_VERSION)
        : base(version)
    {
    }

    protected override void ParseLine(Beatmap beatmap, Section section, string line, bool isPrimaryStream)
    {
        if (section != Section.HitObjects)
        {
            if (line.StartsWith("Mode", StringComparison.Ordinal) && line.Split(':').ElementAtOrDefault(1)?.Trim() == "111")
            {
                beatmap.BeatmapInfo.Ruleset = new UtaRuleset().RulesetInfo;
                return;
            }

            base.ParseLine(beatmap, section, line, isPrimaryStream);
            return;
        }

        if (line.StartsWith("@utaconfig=", StringComparison.OrdinalIgnoreCase))
            metadata = line[(line.IndexOf('=') + 1)..];
        else if (line.StartsWith("@utanote=", StringComparison.OrdinalIgnoreCase))
            notes.Add(line[(line.IndexOf('=') + 1)..]);
        else if (line.Equals("end", StringComparison.OrdinalIgnoreCase))
            finish(beatmap);
    }

    private void finish(Beatmap beatmap)
    {
        beatmap.HitObjects.Clear();

        if (metadata != null)
            beatmap.HitObjects.Add(new UtaMetadataHitObject { Metadata = decode<UtaBeatmapMetadata>(metadata) });

        foreach (UtaPitchNote source in notes.Select(decode<UtaPitchNote>))
        {
            beatmap.HitObjects.Add(new UtaNote
            {
                StartTime = source.Start * 1000,
                Duration = (source.End - source.Start) * 1000,
                Midi = source.Midi,
                NoteKind = source.Kind switch
                {
                    UtaPitchNoteKind.GoldenRap => "golden_rap",
                    _ => source.Kind.ToString().ToLowerInvariant(),
                },
            });
        }
    }

    private static T decode<T>(string encoded)
    {
        try
        {
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return JsonSerializer.Deserialize<T>(json, UtzPackage.JsonOptions)
                   ?? throw new FormatException($"Empty Uta payload for {typeof(T).Name}.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new FormatException($"Invalid Uta payload for {typeof(T).Name}.", ex);
        }
    }
}
