// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Recording;

/// <summary>
/// Exports complete takes or frame ranges from PCM16 WAV files produced by
/// <see cref="UtaWavPcm16Writer"/>.
/// </summary>
public static class UtaRecordingExportService
{
    private const int header_size = 44;

    public static async Task ExportCompleteAsync(
        string source,
        string destination,
        CancellationToken cancellationToken = default)
    {
        string target = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + $".tmp-{Guid.NewGuid():N}";

        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(true);
            }

            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static async Task ExportFrameRangeAsync(
        string source,
        string destination,
        long startFrame,
        long frameCount,
        CancellationToken cancellationToken = default)
    {
        if (startFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        if (frameCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameCount));

        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.RandomAccess);
        byte[] header = new byte[header_size];
        await input.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !header.AsSpan(8, 4).SequenceEqual("WAVE"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(20, 2)) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(34, 2)) != 16)
            throw new InvalidDataException("Only canonical PCM16 WAV files are supported.");

        int channels = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(22, 2));
        int sampleRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24, 4)));
        int blockAlign = channels * 2;
        long availableFrames = (input.Length - header_size) / blockAlign;
        if (startFrame >= availableFrames)
            throw new ArgumentOutOfRangeException(nameof(startFrame));

        frameCount = Math.Min(frameCount, availableFrames - startFrame);
        string target = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + $".tmp-{Guid.NewGuid():N}";

        try
        {
            using var writer = new UtaWavPcm16Writer(temporary, sampleRate, channels);
            input.Position = header_size + startFrame * blockAlign;

            byte[] bytes = new byte[64 * 1024];
            long bytesRemaining = frameCount * blockAlign;
            while (bytesRemaining > 0)
            {
                int requested = (int)Math.Min(bytes.Length, bytesRemaining);
                int read = await input.ReadAsync(bytes.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    throw new EndOfStreamException();

                // Convert the existing PCM16 bytes back to float only through a bounded block.
                // This keeps a single validated writer responsible for the output header.
                int sampleCount = read / 2;
                float[] samples = new float[sampleCount];
                for (int i = 0; i < sampleCount; i++)
                {
                    short value = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2));
                    samples[i] = value < 0 ? value / 32768f : value / 32767f;
                }

                writer.Write(samples);
                bytesRemaining -= read;
            }

            writer.Finalise();
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
