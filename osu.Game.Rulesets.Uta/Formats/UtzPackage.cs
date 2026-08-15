// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace osu.Game.Rulesets.Uta.Formats;

public sealed partial class UtzPackage
{
    public const int MAX_FILES = 128;
    public const long MAX_FILE_BYTES = 512L * 1024 * 1024;
    public const long MAX_TOTAL_BYTES = 2L * 1024 * 1024 * 1024;

    private const string manifest_path = "manifest.json";

    private static readonly HashSet<string> supported_features = new(StringComparer.Ordinal) { "vocal-chart/1" };

    private readonly IReadOnlyDictionary<string, byte[]> files;

    public UtzManifest Manifest { get; }

    public UtaVocalChart? VocalChart { get; }

    public UtaTranscript Transcript { get; }

    public UtaPitchTrack PitchTrack { get; }

    public IReadOnlyList<UtaPitchNote> PitchNotes { get; }

    private UtzPackage(UtzManifest manifest, IReadOnlyDictionary<string, byte[]> files)
    {
        Manifest = manifest;
        this.files = files;

        if (manifest.IsFormatV2)
        {
            UtzAsset vocalAsset = manifest.Charts.Vocal ?? throw invalid("charts.vocal is missing");
            VocalChart = parseJson<UtaVocalChart>(vocalAsset, "vocal chart");
            validateVocalChart(VocalChart);

            (PitchNotes, IReadOnlyList<UtaTranscriptSegment> segments) = projectVocalChart(VocalChart);
            Transcript = new UtaTranscript { Segments = segments };
            PitchTrack = new UtaPitchTrack();
        }
        else
        {
            Transcript = parseJson<UtaTranscript>(manifest.Charts.Transcript ?? throw invalid("charts.transcript is missing"), "transcript");
            PitchTrack = parseJson<UtaPitchTrack>(manifest.Charts.PitchTrack ?? throw invalid("charts.pitch_track is missing"), "pitch track");
            PitchNotes = parseJson<UtaPitchNoteChart>(manifest.Charts.PitchNotes ?? throw invalid("charts.pitch_notes is missing"), "pitch notes").Notes;
        }

        validateCharts();
    }

    public static UtzPackage Open(string path)
    {
        using var stream = File.OpenRead(path);
        return Open(stream);
    }

    public static UtzPackage Open(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);

        if (archive.Entries.Count > MAX_FILES)
            throw invalid($"package contains more than {MAX_FILES} files");

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            validatePackagePath(entry.FullName);

            if (!entries.TryAdd(entry.FullName, entry))
                throw invalid($"duplicate archive entry: {entry.FullName}");
            if (entry.Length > MAX_FILE_BYTES)
                throw invalid($"asset is too large: {entry.FullName}");

            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MAX_TOTAL_BYTES)
                throw invalid("package uncompressed size exceeds the safety limit");
        }

        if (!entries.TryGetValue(manifest_path, out var manifestEntry))
            throw invalid("manifest.json is missing from the archive root");

        UtzManifest manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<UtzManifest>(readEntry(manifestEntry), jsonOptions)
                       ?? throw invalid("manifest.json is empty");
        }
        catch (JsonException ex)
        {
            throw invalid($"invalid manifest JSON: {ex.Message}", ex);
        }

        validateManifest(manifest);

        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var asset in manifest.Assets)
        {
            if (!entries.TryGetValue(asset.Path, out var entry))
                throw invalid($"manifest asset is missing: {asset.Path}");

            byte[] bytes = readEntry(entry);
            if (bytes.LongLength != asset.Bytes)
                throw invalid($"asset byte count does not match the manifest: {asset.Path}");

            string digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(digest, asset.Sha256, StringComparison.Ordinal))
                throw invalid($"asset SHA-256 does not match the manifest: {asset.Path}");

            files.Add(asset.Path, bytes);
        }

        return new UtzPackage(manifest, files);
    }

    public ReadOnlyMemory<byte> GetAsset(UtzAsset asset)
        => files.TryGetValue(asset.Path, out byte[]? bytes)
            ? bytes
            : throw invalid($"asset was not loaded: {asset.Path}");

    private T parseJson<T>(UtzAsset asset, string role)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(GetAsset(asset).Span, jsonOptions)
                   ?? throw invalid($"{role} is empty");
        }
        catch (JsonException ex)
        {
            throw invalid($"invalid {role}: {ex.Message}", ex);
        }
    }

    private void validateCharts()
    {
        if (!Manifest.IsFormatV2)
        {
            if (!double.IsFinite(PitchTrack.HopSeconds) || PitchTrack.HopSeconds <= 0)
                throw invalid("pitch track hop_seconds must be positive");
            if (!PitchTrack.Frames.Zip(PitchTrack.Frames.Skip(1)).All(pair => pair.First.Time <= pair.Second.Time))
                throw invalid("pitch track frames are not time ordered");
        }

        if (PitchNotes.Any(note => !double.IsFinite(note.Start) || !double.IsFinite(note.End) || note.End <= note.Start || note.Midi is < 0 or > 127))
            throw invalid("pitch notes contain an invalid interval or MIDI value");
        if (!PitchNotes.Zip(PitchNotes.Skip(1)).All(pair => pair.First.Start <= pair.Second.Start))
            throw invalid("pitch notes are not time ordered");
        if (Transcript.Segments.Any(segment => !double.IsFinite(segment.Start) || !double.IsFinite(segment.End) || segment.End < segment.Start))
            throw invalid("transcript contains an invalid interval");
    }

    private static void validateVocalChart(UtaVocalChart chart)
    {
        if (chart.Format != "uta.vocal-chart")
            throw invalid($"unsupported vocal chart format: {chart.Format}");
        if (!vocalChartVersionRegex().IsMatch(chart.FormatVersion))
            throw invalid($"unsupported vocal chart version: {chart.FormatVersion}");
        if (chart.Timebase < 1)
            throw invalid("vocal chart timebase must be positive");
        if (chart.Tracks.Count == 0)
            throw invalid("vocal chart has no tracks");

        var parts = new SortedSet<int>();

        foreach (UtaVocalTrack track in chart.Tracks)
        {
            if (track.Phrases.Count == 0)
                throw invalid($"vocal chart track '{track.Id}' has no phrases");
            if (track.Part is { } part)
                parts.Add(part);

            long? previousEnd = null;

            foreach (UtaVocalPhrase phrase in track.Phrases)
            {
                if (phrase.Notes.Count == 0)
                    throw invalid($"vocal chart phrase '{phrase.Id}' has no notes");

                foreach (UtaVocalNote note in phrase.Notes)
                {
                    if (note.Duration < 1)
                        throw invalid($"vocal chart note '{note.Id}' has a non-positive duration");
                    if (note.Pitch is { } pitch && pitch.Midi is < 0 or > 127)
                        throw invalid($"vocal chart note '{note.Id}' has an invalid MIDI pitch");
                    if (note.Scoring.Mode == UtaVocalScoringMode.Pitch && note.Pitch == null)
                        throw invalid($"vocal chart note '{note.Id}' uses pitch scoring but has no pitch target");
                    if (note.Lyrics.Count == 0)
                        throw invalid($"vocal chart note '{note.Id}' has no lyric tokens");
                    if (previousEnd is { } end && note.Start < end)
                        throw invalid($"vocal chart track '{track.Id}' has overlapping or unordered notes");

                    previousEnd = note.Start + note.Duration;
                }
            }
        }

        if (parts.Count > 0 && !parts.SequenceEqual(Enumerable.Range(1, parts.Max)))
            throw invalid("vocal chart duet parts must be contiguous starting at 1");
    }

    /// <summary>
    /// Flattens one playable track of a 0.2 vocal chart into the same
    /// pitch-note/transcript shape 0.1 packages already produce, so the rest
    /// of the pipeline (beatmap conversion, lyrics timeline) does not need to
    /// know which format version a package came from.
    /// </summary>
    private static (IReadOnlyList<UtaPitchNote> Notes, IReadOnlyList<UtaTranscriptSegment> Segments) projectVocalChart(UtaVocalChart chart)
    {
        UtaVocalTrack track = selectPlayableTrack(chart.Tracks);
        double timebase = chart.Timebase;
        double toSeconds(long units) => units / timebase;

        var notes = new List<UtaPitchNote>();
        var segments = new List<UtaTranscriptSegment>();
        var wordsById = new Dictionary<string, UtaTranscriptWord>(StringComparer.Ordinal);

        foreach (UtaVocalPhrase phrase in track.Phrases)
        {
            var words = new List<UtaTranscriptWord>();
            var text = new StringBuilder();

            foreach (UtaVocalNote note in phrase.Notes)
            {
                double start = toSeconds(note.Start);
                double end = toSeconds(note.Start + note.Duration);

                notes.Add(new UtaPitchNote
                {
                    Start = start,
                    End = end,
                    Midi = note.Scoring.Mode == UtaVocalScoringMode.Pitch ? note.Pitch?.Midi : null,
                    Confidence = 1,
                    Kind = classify(note.VocalMode, note.Bonus),
                });

                foreach (UtaLyricToken token in note.Lyrics)
                {
                    if (token.IsContinuation)
                    {
                        if (token.ContinuationOf != null && wordsById.TryGetValue(token.ContinuationOf, out UtaTranscriptWord? existing))
                            existing.End = Math.Max(existing.End, end);
                        continue;
                    }

                    if (string.IsNullOrEmpty(token.Id))
                        continue;

                    if (token.JoinBefore == UtaLyricJoin.Space && text.Length > 0)
                        text.Append(' ');
                    text.Append(token.Text);

                    var word = new UtaTranscriptWord
                    {
                        Word = token.Text ?? string.Empty,
                        Start = start,
                        End = end,
                        Reading = token.Reading,
                        Estimated = false,
                    };
                    words.Add(word);
                    wordsById[token.Id] = word;
                }
            }

            if (words.Count == 0)
                continue;

            segments.Add(new UtaTranscriptSegment
            {
                Text = text.ToString(),
                Start = words.Min(word => word.Start),
                End = words.Max(word => word.End),
                Words = words,
            });
        }

        return (notes, segments);
    }

    private static UtaVocalTrack selectPlayableTrack(IReadOnlyList<UtaVocalTrack> tracks)
        => tracks.FirstOrDefault(track => track.Role == UtaVocalTrackRole.Lead && track.Part is null or 1)
           ?? tracks.FirstOrDefault(track => track.Part is null or 1)
           ?? tracks[0];

    private static UtaPitchNoteKind classify(UtaVocalMode mode, UtaVocalBonus bonus)
    {
        bool golden = bonus == UtaVocalBonus.Golden;

        return mode switch
        {
            UtaVocalMode.Freestyle => golden ? UtaPitchNoteKind.GoldenFreestyle : UtaPitchNoteKind.Freestyle,
            UtaVocalMode.Rap => golden ? UtaPitchNoteKind.GoldenRap : UtaPitchNoteKind.Rap,
            UtaVocalMode.Spoken => golden ? UtaPitchNoteKind.GoldenSpoken : UtaPitchNoteKind.Spoken,
            _ => golden ? UtaPitchNoteKind.Golden : UtaPitchNoteKind.Normal,
        };
    }

    private static void validateManifest(UtzManifest manifest)
    {
        if (manifest.Format != "uta.song")
            throw invalid($"unsupported format: {manifest.Format}");
        if (!versionRegex().IsMatch(manifest.FormatVersion))
            throw invalid($"unsupported format version: {manifest.FormatVersion}");
        if (string.IsNullOrWhiteSpace(manifest.PackageId) || string.IsNullOrWhiteSpace(manifest.Song.Title) || string.IsNullOrWhiteSpace(manifest.Song.Artist))
            throw invalid("package_id, song title, and artist are required");
        if (manifest.Revision < 1 || !double.IsFinite(manifest.Song.DurationSeconds) || manifest.Song.DurationSeconds < 0)
            throw invalid("revision or song duration is invalid");

        if (manifest.IsFormatV2)
        {
            if (manifest.Charts.Vocal == null)
                throw invalid("charts.vocal is required for UTZ 0.2 packages");
            if (!manifest.RequiredFeatures.Contains("vocal-chart/1"))
                throw invalid("required_features must include vocal-chart/1");

            foreach (string feature in manifest.RequiredFeatures)
            {
                if (!supported_features.Contains(feature))
                    throw invalid($"unsupported required feature: {feature}");
            }
        }
        else
        {
            if (manifest.Charts.Transcript == null || manifest.Charts.PitchTrack == null || manifest.Charts.PitchNotes == null)
                throw invalid("charts.transcript, pitch_track, and pitch_notes are required for UTZ 0.1 packages");
            // 0.1's scoring block is required and its engine/version are load-bearing; 0.2's is
            // optional and advisory (a consumer MUST NOT reject a package over an unknown engine).
            if (manifest.Scoring == null || manifest.Scoring.Engine != "uta.pitch" || manifest.Scoring.Version != 1)
                throw invalid($"unsupported scoring engine {manifest.Scoring?.Engine} version {manifest.Scoring?.Version}");
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in manifest.Assets)
        {
            validatePackagePath(asset.Path);
            if (asset.Path == manifest_path)
                throw invalid("manifest.json cannot be used as an asset");
            if (string.IsNullOrWhiteSpace(asset.MediaType))
                throw invalid($"asset has no media type: {asset.Path}");
            if (asset.Bytes is < 0 or > MAX_FILE_BYTES)
                throw invalid($"asset byte count is invalid: {asset.Path}");
            if (!shaRegex().IsMatch(asset.Sha256))
                throw invalid($"asset has an invalid SHA-256: {asset.Path}");
            if (!paths.Add(asset.Path))
                throw invalid($"asset path is used more than once: {asset.Path}");
        }
    }

    private static void validatePackagePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] == '/' || path.Contains('\\') || path.Contains(':'))
            throw invalid($"invalid package path: {path}");

        string[] components = path.Split('/');
        if (components.Any(component => component is "" or "." or ".."))
            throw invalid($"invalid package path: {path}");
    }

    private static byte[] readEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > int.MaxValue)
            throw invalid($"asset cannot be represented in memory: {entry.FullName}");

        using Stream input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        byte[] buffer = new byte[81920];
        long readTotal = 0;

        while (true)
        {
            int read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;

            readTotal += read;
            if (readTotal > MAX_FILE_BYTES)
                throw invalid($"asset expanded beyond the safety limit: {entry.FullName}");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static InvalidDataException invalid(string message, Exception? inner = null) => new($"Invalid .utz package: {message}", inner);

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static JsonSerializerOptions jsonOptions => JsonOptions;

    [GeneratedRegex("^0\\.[12]\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex versionRegex();

    [GeneratedRegex("^1\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex vocalChartVersionRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex shaRegex();
}
