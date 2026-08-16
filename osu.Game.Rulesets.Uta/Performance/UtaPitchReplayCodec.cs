// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Performance;

public static class UtaPitchReplayCodec
{
    public const int MAX_FRAMES = 5_000_000;
    public const int MAX_LINE_CHARACTERS = 512;
    public const long MAX_COMPRESSED_BYTES = 256L * 1024 * 1024;

    public static async Task WriteAsync(string path, IEnumerable<UtaPerformancePitchFrame> frames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        UtaPerformancePitchFrame[] ordered = frames.OrderBy(frame => frame.TimelineEpoch)
                                                    .ThenBy(frame => frame.TimeMicroseconds)
                                                    .ThenByDescending(frame => frame.Voiced)
                                                    .ThenByDescending(frame => frame.ClarityPermille)
                                                    .ToArray();
        if (ordered.Length > MAX_FRAMES)
            throw new InvalidDataException($"Pitch replay contains more than {MAX_FRAMES:N0} frames.");
        validate(ordered);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var compressed = new BrotliStream(file, CompressionLevel.Optimal, false);
        await using var writer = new StreamWriter(compressed, new UTF8Encoding(false), 81920, false);

        foreach (UtaPerformancePitchFrame frame in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string json = JsonSerializer.Serialize(ReplayLine.FromFrame(frame), UtaPerformanceJson.CompactOptions);
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<IReadOnlyList<UtaPerformancePitchFrame>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var information = new FileInfo(path);
        if (!information.Exists)
            throw new FileNotFoundException("Pitch replay was not found.", path);
        if (information.Length > MAX_COMPRESSED_BYTES)
            throw new InvalidDataException("Pitch replay exceeds the compressed-size safety limit.");

        var result = new List<UtaPerformancePitchFrame>();
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var compressed = new BrotliStream(file, CompressionMode.Decompress, false);
        using var reader = new StreamReader(compressed, Encoding.UTF8, true, 81920, false);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.Length > MAX_LINE_CHARACTERS)
                throw new InvalidDataException("Pitch replay contains an oversized JSON record.");
            if (result.Count >= MAX_FRAMES)
                throw new InvalidDataException($"Pitch replay contains more than {MAX_FRAMES:N0} frames.");

            try
            {
                ReplayLine decoded = JsonSerializer.Deserialize<ReplayLine>(line, UtaPerformanceJson.CompactOptions)
                                     ?? throw new InvalidDataException("Pitch replay contains an empty JSON record.");
                result.Add(decoded.ToFrame());
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("Pitch replay contains invalid JSON.", ex);
            }
        }

        validate(result);
        return result;
    }

    private static void validate(IEnumerable<UtaPerformancePitchFrame> frames)
    {
        int previousEpoch = -1;
        long previousTime = -1;
        foreach (UtaPerformancePitchFrame frame in frames)
        {
            if (frame.TimelineEpoch < 0 || frame.TimeMicroseconds < 0)
                throw new InvalidDataException("Pitch replay contains a negative epoch or time.");
            if (frame.ClarityPermille > 1000)
                throw new InvalidDataException("Pitch replay clarity is outside 0-1000.");
            if (frame.Voiced && frame.PitchCents is < 0 or > 12_700)
                throw new InvalidDataException("Pitch replay contains a voiced pitch outside MIDI 0-127.");
            if (frame.RmsDecibelsTenths is < -1_200 or > 120)
                throw new InvalidDataException("Pitch replay RMS is outside the supported -120.0 to +12.0 dB range.");
            if (frame.TimelineEpoch < previousEpoch
                || frame.TimelineEpoch == previousEpoch && frame.TimeMicroseconds < previousTime)
                throw new InvalidDataException("Pitch replay frames are not ordered by epoch and time.");

            previousEpoch = frame.TimelineEpoch;
            previousTime = frame.TimeMicroseconds;
        }
    }

    private sealed record ReplayLine
    {
        [JsonPropertyName("t")]
        public long TimeMicroseconds { get; init; }

        [JsonPropertyName("p")]
        public int PitchCents { get; init; }

        [JsonPropertyName("c")]
        public ushort ClarityPermille { get; init; }

        [JsonPropertyName("r")]
        public short? RmsDecibelsTenths { get; init; }

        [JsonPropertyName("v")]
        public bool Voiced { get; init; }

        [JsonPropertyName("e")]
        public int TimelineEpoch { get; init; }

        public static ReplayLine FromFrame(UtaPerformancePitchFrame frame)
            => new()
            {
                TimeMicroseconds = frame.TimeMicroseconds,
                PitchCents = frame.PitchCents,
                ClarityPermille = frame.ClarityPermille,
                RmsDecibelsTenths = frame.RmsDecibelsTenths,
                Voiced = frame.Voiced,
                TimelineEpoch = frame.TimelineEpoch,
            };

        public UtaPerformancePitchFrame ToFrame()
            => new(TimeMicroseconds, PitchCents, ClarityPermille, RmsDecibelsTenths, Voiced, TimelineEpoch);
    }
}
