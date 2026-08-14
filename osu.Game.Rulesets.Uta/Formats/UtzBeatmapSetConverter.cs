// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using osu.Game.Rulesets.Uta.UI;

namespace osu.Game.Rulesets.Uta.Formats;

/// <summary>
/// Converts a validated UTZ package into the standard beatmap archive consumed by osu!lazer.
/// All declared media assets are copied, including cover artwork and background video.
/// </summary>
public static class UtzBeatmapSetConverter
{
    public const string BEATMAP_FILENAME = "uta.osu";

    public static void Convert(string inputPath, string outputPath)
    {
        using var input = File.OpenRead(inputPath);
        using var output = File.Create(outputPath);
        Convert(input, output);
    }

    public static void Convert(Stream input, Stream output)
    {
        var package = UtzPackage.Open(input);

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, true);
        addText(archive, BEATMAP_FILENAME, createBeatmap(package));
        addText(archive, "manifest.json", JsonSerializer.Serialize(package.Manifest, indented_json_options));

        foreach (var asset in package.Manifest.Assets)
            addBytes(archive, asset.Path, package.GetAsset(asset).Span);
    }

    internal static string CreateBeatmap(UtzPackage package) => createBeatmap(package);

    private static string createBeatmap(UtzPackage package)
    {
        var manifest = package.Manifest;
        var segments = UtaLyricsTimeline.Normalize(package.Transcript.Segments);
        int centreMidi = calculateCentreMidi(package.PitchNotes);
        string? cover = manifest.Visuals.Cover?.Path;
        string? video = manifest.Visuals.Video?.Path;
        double bpm = manifest.Song.Bpm.GetValueOrDefault(120);
        if (!double.IsFinite(bpm) || bpm <= 0)
            bpm = 120;

        var lines = new List<string>
        {
            "uta file format v1",
            string.Empty,
            "[General]",
            $"AudioFilename: {safeValue(manifest.Audio.Instrumental.Path)}",
            $"AudioLeadIn: {(int)Math.Round(manifest.Audio.AudioOffsetSeconds * 1000)}",
            $"PreviewTime: {(int)Math.Round(segments.FirstOrDefault()?.Start * 1000 ?? 0)}",
            "Countdown: 0",
            "SampleSet: Normal",
            "StackLeniency: 0.7",
            "Mode: 111",
            "LetterboxInBreaks: 0",
            "WidescreenStoryboard: 1",
            string.Empty,
            "[Metadata]",
            $"Title:{safeValue(manifest.Song.Title)}",
            $"TitleUnicode:{safeValue(manifest.Song.Title)}",
            $"Artist:{safeValue(manifest.Song.Artist)}",
            $"ArtistUnicode:{safeValue(manifest.Song.Artist)}",
            $"Creator:{safeValue(manifest.Provenance.Generator ?? "Uta Studio")}",
            "Version:Uta",
            $"Source:{safeValue(manifest.Song.Album ?? manifest.Song.Key ?? string.Empty)}",
            "Tags:uta utz",
            "BeatmapID:0",
            "BeatmapSetID:-1",
            string.Empty,
            "[Difficulty]",
            "HPDrainRate:5",
            "CircleSize:5",
            "OverallDifficulty:5",
            "ApproachRate:5",
            "SliderMultiplier:1",
            "SliderTickRate:1",
            string.Empty,
            "[Events]",
            "//Background and Video events",
        };

        if (video != null)
            lines.Add($"Video,0,\"{safeEventPath(video)}\"");
        if (cover != null)
            lines.Add($"0,0,\"{safeEventPath(cover)}\",0,0");

        foreach (var gap in UtaGapSkipController.FindSkippableGaps(segments, package.PitchNotes))
            lines.Add($"2,{(int)Math.Round(gap.StartTime)},{(int)Math.Round(gap.EndTime)}");

        lines.AddRange(new[]
        {
            string.Empty,
            "[TimingPoints]",
            $"0,{(60000 / bpm).ToString("0.########", CultureInfo.InvariantCulture)},4,2,1,100,1,0",
            string.Empty,
            "[Colours]",
            string.Empty,
            "[HitObjects]",
        });

        var config = new UtaBeatmapMetadata
        {
            PackageId = manifest.PackageId,
            OctaveTolerance = manifest.Scoring.OctaveTolerance,
            GuideVocalsFile = manifest.Audio.GuideVocals?.Path,
            OriginalAudioFile = manifest.Audio.Original?.Path,
            CentreMidi = centreMidi,
            Transcript = segments,
        };

        lines.Add($"@utaconfig={encode(config)}");
        lines.AddRange(package.PitchNotes.Select(note => $"@utanote={encode(note)}"));
        lines.Add("end");

        return string.Join('\n', lines) + '\n';
    }

    private static int calculateCentreMidi(IReadOnlyList<UtaPitchNote> notes)
    {
        if (notes.Count == 0)
            return 60;

        int median = notes.Select(note => note.Midi).Order().ElementAt(notes.Count / 2);
        return (int)Math.Round(median / 12.0) * 12;
    }

    private static string encode(object value)
    {
        string json = JsonSerializer.Serialize(value, json_options);
        return System.Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static string safeValue(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string safeEventPath(string value) => safeValue(value).Replace("\"", string.Empty);

    private static void addText(ZipArchive archive, string path, string value)
        => addBytes(archive, path, Encoding.UTF8.GetBytes(value));

    private static void addBytes(ZipArchive archive, string path, ReadOnlySpan<byte> bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using Stream output = entry.Open();
        output.Write(bytes);
    }

    private static readonly JsonSerializerOptions json_options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static readonly JsonSerializerOptions indented_json_options = new(json_options) { WriteIndented = true };
}
