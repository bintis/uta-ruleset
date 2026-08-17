// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Buffers.Binary;
using System.IO;

namespace osu.Game.Rulesets.Uta.Recording;

/// <summary>
/// Streaming PCM16 little-endian RIFF/WAVE writer. Intended to be called only
/// from the recording background consumer, never from the microphone callback.
/// </summary>
public sealed class UtaWavPcm16Writer : IDisposable
{
    private const int header_size = 44;
    private readonly FileStream stream;
    private readonly byte[] conversionBuffer = new byte[64 * 1024];
    private bool finalised;

    public int SampleRate { get; }
    public int Channels { get; }
    public long FramesWritten { get; private set; }
    public long ClippedSamples { get; private set; }

    public UtaWavPcm16Writer(string path, int sampleRate, int channels)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A WAV output path is required.", nameof(path));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0 || channels > 32)
            throw new ArgumentOutOfRangeException(nameof(channels));

        SampleRate = sampleRate;
        Channels = channels;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        stream.Write(new byte[header_size]);
    }

    public void Write(ReadOnlySpan<float> interleavedSamples)
    {
        if (finalised)
            throw new InvalidOperationException("The WAV writer has already been finalised.");
        if (interleavedSamples.Length % Channels != 0)
            throw new ArgumentException("PCM buffer contains a partial frame.", nameof(interleavedSamples));

        int offset = 0;
        while (offset < interleavedSamples.Length)
        {
            int sampleCount = Math.Min(conversionBuffer.Length / 2, interleavedSamples.Length - offset);
            Span<byte> output = conversionBuffer.AsSpan(0, sampleCount * 2);

            for (int i = 0; i < sampleCount; i++)
            {
                float sample = interleavedSamples[offset + i];
                if (!float.IsFinite(sample))
                    sample = 0;

                if (sample > 1)
                {
                    sample = 1;
                    ClippedSamples++;
                }
                else if (sample < -1)
                {
                    sample = -1;
                    ClippedSamples++;
                }

                int scaled = sample <= -1
                    ? short.MinValue
                    : (int)Math.Round(sample * short.MaxValue, MidpointRounding.AwayFromZero);
                BinaryPrimitives.WriteInt16LittleEndian(output.Slice(i * 2, 2), checked((short)scaled));
            }

            stream.Write(output);
            offset += sampleCount;
        }

        FramesWritten += interleavedSamples.Length / Channels;

        // Classic RIFF uses 32-bit chunk sizes. Fail before producing a corrupt file.
        if (stream.Length - 8 > uint.MaxValue)
            throw new IOException("WAV recording exceeded the RIFF 4 GiB size limit.");
    }

    public void Finalise()
    {
        if (finalised)
            return;

        long dataBytes = stream.Length - header_size;
        if (dataBytes > uint.MaxValue || stream.Length - 8 > uint.MaxValue)
            throw new IOException("WAV recording exceeded the RIFF 4 GiB size limit.");

        Span<byte> header = stackalloc byte[header_size];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), checked((uint)(stream.Length - 8)));
        "WAVE"u8.CopyTo(header.Slice(8));
        "fmt "u8.CopyTo(header.Slice(12));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(22, 2), checked((ushort)Channels));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(24, 4), checked((uint)SampleRate));
        uint byteRate = checked((uint)(SampleRate * Channels * 2));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(28, 4), byteRate);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(32, 2), checked((ushort)(Channels * 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(34, 2), 16);
        "data"u8.CopyTo(header.Slice(36));
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(40, 4), checked((uint)dataBytes));

        stream.Position = 0;
        stream.Write(header);
        stream.Flush(true);
        finalised = true;
    }

    public void Dispose()
    {
        try
        {
            Finalise();
        }
        finally
        {
            stream.Dispose();
        }
    }

    /// <summary>
    /// Repairs a provisional PCM16 file created by this writer when the process
    /// terminated after writing audio but before the final header update.
    /// </summary>
    public static bool TryRepair(string path, int sampleRate, int channels)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (stream.Length < header_size)
                return false;

            long dataBytes = stream.Length - header_size;
            if (dataBytes > uint.MaxValue || stream.Length - 8 > uint.MaxValue)
                return false;

            Span<byte> header = stackalloc byte[header_size];
            "RIFF"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), checked((uint)(stream.Length - 8)));
            "WAVE"u8.CopyTo(header.Slice(8));
            "fmt "u8.CopyTo(header.Slice(12));
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(16, 4), 16);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(20, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(22, 2), checked((ushort)channels));
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(24, 4), checked((uint)sampleRate));
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(28, 4), checked((uint)(sampleRate * channels * 2)));
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(32, 2), checked((ushort)(channels * 2)));
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(34, 2), 16);
            "data"u8.CopyTo(header.Slice(36));
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(40, 4), checked((uint)dataBytes));

            stream.Position = 0;
            stream.Write(header);
            stream.Flush(true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
