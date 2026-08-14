// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
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

    private readonly IReadOnlyDictionary<string, byte[]> files;

    public UtzManifest Manifest { get; }

    public UtaTranscript Transcript { get; }

    public UtaPitchTrack PitchTrack { get; }

    public IReadOnlyList<UtaPitchNote> PitchNotes { get; }

    private UtzPackage(UtzManifest manifest, IReadOnlyDictionary<string, byte[]> files)
    {
        Manifest = manifest;
        this.files = files;

        Transcript = parseJson<UtaTranscript>(manifest.Charts.Transcript, "transcript");
        PitchTrack = parseJson<UtaPitchTrack>(manifest.Charts.PitchTrack, "pitch track");
        PitchNotes = parseJson<UtaPitchNoteChart>(manifest.Charts.PitchNotes, "pitch notes").Notes;

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
        if (!double.IsFinite(PitchTrack.HopSeconds) || PitchTrack.HopSeconds <= 0)
            throw invalid("pitch track hop_seconds must be positive");
        if (!PitchTrack.Frames.Zip(PitchTrack.Frames.Skip(1)).All(pair => pair.First.Time <= pair.Second.Time))
            throw invalid("pitch track frames are not time ordered");
        if (PitchNotes.Any(note => !double.IsFinite(note.Start) || !double.IsFinite(note.End) || note.End <= note.Start || note.Midi is < 0 or > 127))
            throw invalid("pitch notes contain an invalid interval or MIDI value");
        if (!PitchNotes.Zip(PitchNotes.Skip(1)).All(pair => pair.First.Start <= pair.Second.Start))
            throw invalid("pitch notes are not time ordered");
        if (Transcript.Segments.Any(segment => !double.IsFinite(segment.Start) || !double.IsFinite(segment.End) || segment.End < segment.Start))
            throw invalid("transcript contains an invalid interval");
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
        if (manifest.Scoring.Engine != "uta.pitch" || manifest.Scoring.Version != 1)
            throw invalid($"unsupported scoring engine {manifest.Scoring.Engine} version {manifest.Scoring.Version}");

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

    [GeneratedRegex("^0\\.1\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex versionRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex shaRegex();
}
