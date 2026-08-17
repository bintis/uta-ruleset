// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Buffers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Recording;

/// <summary>
/// Non-blocking PCM boundary for the microphone callback. The producer only
/// rents/copies and TryWrite()s; all file IO occurs on the single consumer.
/// </summary>
public sealed class UtaPcmCaptureQueue : IAsyncDisposable
{
    private readonly Channel<UtaPcmCaptureBlock> channel;
    private long queuedFrames;
    private long rejectedBlocks;
    private bool completed;

    public long QueuedFrames => Interlocked.Read(ref queuedFrames);
    public long RejectedBlocks => Interlocked.Read(ref rejectedBlocks);

    public UtaPcmCaptureQueue(int capacityBlocks = 256)
    {
        if (capacityBlocks <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacityBlocks));

        channel = Channel.CreateBounded<UtaPcmCaptureBlock>(new BoundedChannelOptions(capacityBlocks)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    }

    public bool TryWrite(
        ReadOnlySpan<float> interleavedSamples,
        int sampleRate,
        int channels,
        long captureEndTimestamp,
        float inputGain)
    {
        if (completed)
            return false;
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));
        if (interleavedSamples.Length == 0 || interleavedSamples.Length % channels != 0)
            throw new ArgumentException("PCM buffer must contain complete interleaved frames.", nameof(interleavedSamples));

        float[] buffer = ArrayPool<float>.Shared.Rent(interleavedSamples.Length);
        interleavedSamples.CopyTo(buffer);
        var block = new UtaPcmCaptureBlock(
            buffer,
            interleavedSamples.Length,
            sampleRate,
            channels,
            captureEndTimestamp,
            inputGain);

        if (!channel.Writer.TryWrite(block))
        {
            block.Dispose();
            Interlocked.Increment(ref rejectedBlocks);
            return false;
        }

        Interlocked.Add(ref queuedFrames, block.FrameCount);
        return true;
    }

    public async ValueTask<UtaPcmCaptureBlock?> ReadAsync(CancellationToken cancellationToken)
    {
        while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (channel.Reader.TryRead(out UtaPcmCaptureBlock? block))
            {
                Interlocked.Add(ref queuedFrames, -block.FrameCount);
                return block;
            }
        }

        return null;
    }

    public void Complete()
    {
        completed = true;
        channel.Writer.TryComplete();
    }

    public async ValueTask DisposeAsync()
    {
        Complete();
        while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (channel.Reader.TryRead(out UtaPcmCaptureBlock? block))
                block.Dispose();
        }
    }
}

public sealed class UtaPcmCaptureBlock : IDisposable
{
    private float[]? samples;

    public int SampleCount { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public long CaptureEndTimestamp { get; }
    public float InputGain { get; }
    public int FrameCount => SampleCount / Channels;
    public ReadOnlySpan<float> Samples => samples.AsSpan(0, SampleCount);

    internal UtaPcmCaptureBlock(
        float[] samples,
        int sampleCount,
        int sampleRate,
        int channels,
        long captureEndTimestamp,
        float inputGain)
    {
        this.samples = samples;
        SampleCount = sampleCount;
        SampleRate = sampleRate;
        Channels = channels;
        CaptureEndTimestamp = captureEndTimestamp;
        InputGain = inputGain;
    }

    public void Dispose()
    {
        float[]? buffer = Interlocked.Exchange(ref samples, null);
        if (buffer != null)
            ArrayPool<float>.Shared.Return(buffer);
    }
}
