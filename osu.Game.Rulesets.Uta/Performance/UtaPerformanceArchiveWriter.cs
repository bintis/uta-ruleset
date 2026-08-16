// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Performance;

public sealed class UtaPerformanceArchiveWriter
{
    public const string MANIFEST_FILENAME = "performance.json";
    public const string PITCH_REPLAY_FILENAME = "pitch-replay.jsonl.br";
    public const string COMPLETE_FILENAME = "complete";

    private readonly UtaPerformancePaths paths;

    public UtaPerformanceArchiveWriter(string rootDirectory)
    {
        paths = new UtaPerformancePaths(rootDirectory);
    }

    public async Task<UtaPerformanceArchiveEntry> WriteAsync(UtaPerformanceWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Manifest);
        ArgumentNullException.ThrowIfNull(request.PitchFrames);

        UtaPerformanceManifest manifest = request.Manifest;
        if (manifest.PerformanceId == Guid.Empty)
            manifest.PerformanceId = Guid.NewGuid();
        if (manifest.SchemaVersion != UtaPerformanceManifest.LATEST_SCHEMA_VERSION)
            throw new InvalidDataException($"Unsupported performance schema version {manifest.SchemaVersion}.");
        if (manifest.Song == null || manifest.Scoring == null || manifest.Judgements == null
            || manifest.Settings == null || manifest.Notes == null || manifest.Phrases == null)
            throw new InvalidDataException("Performance manifest is missing a required section.");

        normaliseEligibility(manifest);

        if (request.Recording != null && manifest.Recording == null)
            throw new InvalidDataException("Recording metadata is required when a recording asset is saved.");
        if (request.Recording == null && manifest.Recording != null)
            throw new InvalidDataException("Recording metadata was supplied without a recording asset.");

        manifest.Files = new UtaPerformanceFileSet();
        manifest.Checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        Directory.CreateDirectory(paths.PerformancesDirectory);
        string finalDirectory = paths.GetPerformanceDirectory(manifest.PerformanceId);
        if (Directory.Exists(finalDirectory))
            throw new IOException($"Performance {manifest.PerformanceId:D} already exists.");

        string partialDirectory = Path.Combine(paths.PerformancesDirectory, $".partial-{manifest.PerformanceId:D}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(partialDirectory);
        bool committed = false;

        try
        {
            string pitchPath = UtaPerformancePaths.ResolveContainedFile(partialDirectory, PITCH_REPLAY_FILENAME);
            await UtaPitchReplayCodec.WriteAsync(pitchPath, request.PitchFrames, cancellationToken).ConfigureAwait(false);
            manifest.Files.PitchReplay = PITCH_REPLAY_FILENAME;
            manifest.Checksums[PITCH_REPLAY_FILENAME] = await sha256Async(pitchPath, cancellationToken).ConfigureAwait(false);

            if (request.Recording != null)
            {
                string recordingFileName = UtaPerformancePaths.ValidateFileName(request.RecordingFileName);
                string recordingPath = UtaPerformancePaths.ResolveContainedFile(partialDirectory, recordingFileName);
                await copyAssetAsync(request.Recording, recordingPath, cancellationToken).ConfigureAwait(false);
                manifest.Files.Recording = recordingFileName;
                manifest.Checksums[recordingFileName] = await sha256Async(recordingPath, cancellationToken).ConfigureAwait(false);
            }

            if (request.Waveform != null)
            {
                string waveformFileName = UtaPerformancePaths.ValidateFileName(request.WaveformFileName);
                string waveformPath = UtaPerformancePaths.ResolveContainedFile(partialDirectory, waveformFileName);
                await copyAssetAsync(request.Waveform, waveformPath, cancellationToken).ConfigureAwait(false);
                manifest.Files.Waveform = waveformFileName;
                manifest.Checksums[waveformFileName] = await sha256Async(waveformPath, cancellationToken).ConfigureAwait(false);
            }

            string temporaryManifest = UtaPerformancePaths.ResolveContainedFile(partialDirectory, MANIFEST_FILENAME + ".tmp");
            await using (var output = new FileStream(temporaryManifest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(output, manifest, UtaPerformanceJson.Options, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(true);
            }

            string manifestPath = UtaPerformancePaths.ResolveContainedFile(partialDirectory, MANIFEST_FILENAME);
            File.Move(temporaryManifest, manifestPath);
            await File.WriteAllTextAsync(UtaPerformancePaths.ResolveContainedFile(partialDirectory, COMPLETE_FILENAME), string.Empty, cancellationToken).ConfigureAwait(false);
            Directory.Move(partialDirectory, finalDirectory);
            committed = true;

            bool indexUpdated = await tryRebuildIndexAsync(paths.RootDirectory).ConfigureAwait(false);
            return new UtaPerformanceArchiveEntry(finalDirectory, manifest, indexUpdated);
        }
        catch
        {
            if (!committed)
                tryDeleteDirectory(partialDirectory);
            throw;
        }
    }

    private static void normaliseEligibility(UtaPerformanceManifest manifest)
    {
        manifest.Eligibility ??= new UtaPerformanceEligibility();
        var reasons = new HashSet<UtaPerformanceInvalidationReason>(manifest.Eligibility.InvalidationReasons ?? Array.Empty<UtaPerformanceInvalidationReason>());
        if (manifest.Settings.PracticeSession)
            reasons.Add(UtaPerformanceInvalidationReason.PracticeSession);

        if (reasons.Count > 0)
        {
            manifest.Eligibility.Comparable = false;
            manifest.Eligibility.InvalidationReasons = new List<UtaPerformanceInvalidationReason>(reasons).OrderBy(reason => reason).ToArray();
        }
        else
        {
            manifest.Eligibility.Comparable = true;
            manifest.Eligibility.InvalidationReasons = Array.Empty<UtaPerformanceInvalidationReason>();
        }
    }

    private static async Task copyAssetAsync(Stream input, string outputPath, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(true);
    }

    private static async Task<bool> tryRebuildIndexAsync(string rootDirectory)
    {
        try
        {
            // The archive is already committed. The index is rebuildable cache,
            // so a caller cancellation or storage race must not turn a successful
            // performance write into a reported failure.
            await UtaPerformanceIndexStore.RebuildAsync(rootDirectory, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void tryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Recovery removes stale .partial directories on the next scan.
        }
    }

    private static async Task<string> sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] digest = await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
