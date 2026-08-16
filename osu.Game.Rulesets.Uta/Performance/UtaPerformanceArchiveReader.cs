// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Performance;

public sealed class UtaPerformanceArchiveReader
{
    public async Task<UtaPerformanceManifest> ReadManifestAsync(string performanceDirectory, bool verifyChecksums = true, CancellationToken cancellationToken = default)
    {
        string directory = Path.GetFullPath(performanceDirectory);
        if (!File.Exists(UtaPerformancePaths.ResolveContainedFile(directory, UtaPerformanceArchiveWriter.COMPLETE_FILENAME)))
            throw new InvalidDataException("Performance archive has no completion marker.");

        string manifestPath = UtaPerformancePaths.ResolveContainedFile(directory, UtaPerformanceArchiveWriter.MANIFEST_FILENAME);
        UtaPerformanceManifest manifest;
        try
        {
            await using var input = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            manifest = await JsonSerializer.DeserializeAsync<UtaPerformanceManifest>(input, UtaPerformanceJson.Options, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidDataException("Performance manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Performance manifest contains invalid JSON.", ex);
        }

        validateManifest(manifest);
        validateAsset(directory, manifest.Files.PitchReplay);
        validateAsset(directory, manifest.Files.Recording);
        validateAsset(directory, manifest.Files.Waveform);
        if ((manifest.Files.Recording == null) != (manifest.Recording == null))
            throw new InvalidDataException("Recording metadata and recording asset must be present together.");

        if (verifyChecksums)
        {
            requireChecksum(manifest, manifest.Files.PitchReplay);
            requireChecksum(manifest, manifest.Files.Recording);
            requireChecksum(manifest, manifest.Files.Waveform);

            foreach (KeyValuePair<string, string> checksum in manifest.Checksums)
            {
                if (!isSha256(checksum.Value))
                    throw new InvalidDataException($"Performance asset has an invalid checksum: {checksum.Key}");

                string asset = UtaPerformancePaths.ResolveContainedFile(directory, checksum.Key);
                if (!File.Exists(asset))
                    throw new FileNotFoundException("A performance asset is missing.", asset);
                string actual = await sha256Async(asset, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actual, checksum.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Performance asset checksum failed: {checksum.Key}");
            }
        }

        return manifest;
    }

    public async Task<IReadOnlyList<UtaPerformancePitchFrame>> ReadPitchReplayAsync(string performanceDirectory, CancellationToken cancellationToken = default)
    {
        UtaPerformanceManifest manifest = await ReadManifestAsync(performanceDirectory, false, cancellationToken).ConfigureAwait(false);
        if (manifest.Files.PitchReplay == null)
            return Array.Empty<UtaPerformancePitchFrame>();

        string path = UtaPerformancePaths.ResolveContainedFile(performanceDirectory, manifest.Files.PitchReplay);
        return await UtaPitchReplayCodec.ReadAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public Stream? OpenRecording(string performanceDirectory, UtaPerformanceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Files.Recording == null)
            return null;

        string path = UtaPerformancePaths.ResolveContainedFile(performanceDirectory, manifest.Files.Recording);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
    }

    private static void requireChecksum(UtaPerformanceManifest manifest, string? relativeName)
    {
        if (relativeName != null && !manifest.Checksums.ContainsKey(relativeName))
            throw new InvalidDataException($"Performance asset has no checksum: {relativeName}");
    }

    private static void validateManifest(UtaPerformanceManifest manifest)
    {
        if (manifest.SchemaVersion != UtaPerformanceManifest.LATEST_SCHEMA_VERSION)
            throw new InvalidDataException($"Unsupported performance schema version {manifest.SchemaVersion}.");
        if (manifest.PerformanceId == Guid.Empty)
            throw new InvalidDataException("Performance manifest has no ID.");
        if (manifest.Song == null || manifest.Scoring == null || manifest.Judgements == null || manifest.Settings == null || manifest.Files == null)
            throw new InvalidDataException("Performance manifest is missing a required section.");
        if (manifest.Checksums == null)
            throw new InvalidDataException("Performance manifest is missing checksums.");
        if (manifest.Eligibility == null || manifest.Eligibility.InvalidationReasons == null)
            throw new InvalidDataException("Performance manifest is missing eligibility metadata.");
        if (manifest.Eligibility.Comparable && manifest.Eligibility.InvalidationReasons.Count > 0
            || !manifest.Eligibility.Comparable && manifest.Eligibility.InvalidationReasons.Count == 0)
            throw new InvalidDataException("Performance eligibility state is inconsistent.");
        if (manifest.Settings.PracticeSession && manifest.Eligibility.Comparable)
            throw new InvalidDataException("A practice performance cannot be marked comparable.");
        if (manifest.Notes == null || manifest.Phrases == null)
            throw new InvalidDataException("Performance manifest is missing note or phrase summaries.");
        if (manifest.Scoring.TotalScore is < 0 or > 1_000_000)
            throw new InvalidDataException("Performance score is outside 0-1,000,000.");
        if (manifest.Scoring.EngineVersion <= 0 || string.IsNullOrWhiteSpace(manifest.Scoring.Engine))
            throw new InvalidDataException("Performance scoring engine metadata is invalid.");
        if (manifest.Scoring.CompositeRatingPermille > 1000
            || manifest.Scoring.PitchAccuracyPermille > 1000
            || manifest.Scoring.CoveragePermille > 1000
            || manifest.Scoring.StabilityPermille > 1000
            || manifest.Scoring.LongToneQualityPermille > 1000
            || manifest.Scoring.VibratoQualityPermille > 1000
            || manifest.Scoring.ExpressionQualityPermille is > 1000)
            throw new InvalidDataException("Performance scoring quality is outside 0-1000.");
        if (manifest.Settings.PlaybackRate is < 0.25 or > 4
            || !double.IsFinite(manifest.Settings.PlaybackRate)
            || !double.IsFinite(manifest.Settings.MicrophoneLatencyMilliseconds)
            || !double.IsFinite(manifest.Settings.PitchSamplingIntervalMilliseconds)
            || !double.IsFinite(manifest.Settings.InputGain))
            throw new InvalidDataException("Performance settings contain an invalid numeric value.");
        if (manifest.Recording != null)
        {
            if (manifest.Recording.SampleRate <= 0 || manifest.Recording.Channels <= 0
                || !double.IsFinite(manifest.Recording.CalibratedLatencyMilliseconds)
                || !double.IsFinite(manifest.Recording.InputGain)
                || string.IsNullOrWhiteSpace(manifest.Recording.Container)
                || string.IsNullOrWhiteSpace(manifest.Recording.SampleFormat)
                || string.IsNullOrWhiteSpace(manifest.Recording.SignalStage))
                throw new InvalidDataException("Performance recording metadata is invalid.");
        }
    }

    private static void validateAsset(string directory, string? relativeName)
    {
        if (relativeName == null)
            return;

        string path = UtaPerformancePaths.ResolveContainedFile(directory, relativeName);
        if (!File.Exists(path))
            throw new FileNotFoundException("A referenced performance asset is missing.", path);
    }

    private static bool isSha256(string value)
    {
        if (value.Length != 64)
            return false;
        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }
        return true;
    }

    private static async Task<string> sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] digest = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
