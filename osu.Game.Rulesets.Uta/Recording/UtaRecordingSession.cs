// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Recording;

public interface IUtaPcmCaptureSink
{
    bool TryWrite(
        ReadOnlySpan<float> interleavedSamples,
        int sampleRate,
        int channels,
        long captureEndTimestamp,
        float appliedInputGain);
}

/// <summary>
/// Owns one recording take. It is deliberately independent from drawable/UI
/// state: the microphone callback writes through <see cref="IUtaPcmCaptureSink"/>,
/// while gameplay supplies timeline segment boundaries.
/// </summary>
public sealed class UtaRecordingSession : IUtaPcmCaptureSink, IAsyncDisposable
{
    private readonly object sync = new();
    private readonly List<UtaRecordingSegment> segments = new();
    private UtaPcmCaptureQueue? queue;
    private UtaWavPcm16Writer? writer;
    private Task? writerTask;
    private long acceptedFrames;
    private string? filePath;
    private int sampleRate;
    private int channels;
    private UtaRecordingState state = UtaRecordingState.Ready;
    private UtaRecordingFaultReason faultReason;
    private string? faultMessage;

    public event Action<UtaRecordingProgress>? ProgressChanged;

    public UtaRecordingState State
    {
        get
        {
            lock (sync)
                return state;
        }
    }

    public UtaRecordingMetadata Metadata { get; private set; } = new();

    public void StartDeferred(
        string path,
        UtaRecordingMetadata metadata,
        int queueCapacityBlocks = 256)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        lock (sync)
        {
            if (state is UtaRecordingState.Recording or UtaRecordingState.Paused or UtaRecordingState.Finalizing)
                throw new InvalidOperationException("A recording take is already active.");

            sampleRate = 0;
            channels = 0;
            filePath = Path.GetFullPath(path);
            Metadata = metadata;
            Metadata.SampleRate = 0;
            Metadata.Channels = 0;
            Metadata.Complete = false;
            Metadata.FaultReason = UtaRecordingFaultReason.None;
            Metadata.FaultMessage = null;

            acceptedFrames = 0;
            segments.Clear();
            faultReason = UtaRecordingFaultReason.None;
            faultMessage = null;
            queue = new UtaPcmCaptureQueue(queueCapacityBlocks);
            writer = null;
            state = UtaRecordingState.Recording;
            writerTask = Task.Run(consumeAsync);
        }

        publishProgress();
    }

    public void Start(
        string path,
        int sampleRate,
        int channels,
        UtaRecordingMetadata metadata,
        int queueCapacityBlocks = 256)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        lock (sync)
        {
            if (state is UtaRecordingState.Recording or UtaRecordingState.Paused or UtaRecordingState.Finalizing)
                throw new InvalidOperationException("A recording take is already active.");

            this.sampleRate = sampleRate;
            this.channels = channels;
            filePath = Path.GetFullPath(path);
            Metadata = metadata;
            Metadata.SampleRate = sampleRate;
            Metadata.Channels = channels;
            Metadata.Complete = false;
            Metadata.FaultReason = UtaRecordingFaultReason.None;
            Metadata.FaultMessage = null;

            acceptedFrames = 0;
            segments.Clear();
            faultReason = UtaRecordingFaultReason.None;
            faultMessage = null;
            queue = new UtaPcmCaptureQueue(queueCapacityBlocks);
            writer = new UtaWavPcm16Writer(filePath, sampleRate, channels);
            state = UtaRecordingState.Recording;
            writerTask = Task.Run(consumeAsync);
        }

        publishProgress();
    }

    public void Pause()
    {
        lock (sync)
        {
            if (state == UtaRecordingState.Recording)
                state = UtaRecordingState.Paused;
        }

        publishProgress();
    }

    public void Resume()
    {
        lock (sync)
        {
            if (state == UtaRecordingState.Paused)
                state = UtaRecordingState.Recording;
        }

        publishProgress();
    }

    public void BeginTimelineSegment(
        long songStartTimeMicroseconds,
        double playbackRate,
        int timelineEpoch,
        UtaRecordingSegmentReason reason)
    {
        if (!double.IsFinite(playbackRate) || playbackRate is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(playbackRate));
        if (timelineEpoch < 0)
            throw new ArgumentOutOfRangeException(nameof(timelineEpoch));

        int rateMillionths = checked((int)Math.Round(
            playbackRate * 1_000_000,
            MidpointRounding.AwayFromZero));

        lock (sync)
        {
            if (state is not (UtaRecordingState.Recording or UtaRecordingState.Paused))
                return;

            segments.Add(new UtaRecordingSegment(
                Interlocked.Read(ref acceptedFrames),
                0,
                songStartTimeMicroseconds,
                rateMillionths,
                timelineEpoch,
                reason));
        }
    }

    bool IUtaPcmCaptureSink.TryWrite(
        ReadOnlySpan<float> interleavedSamples,
        int inputSampleRate,
        int inputChannels,
        long captureEndTimestamp,
        float appliedInputGain)
        => TryWrite(interleavedSamples, inputSampleRate, inputChannels, captureEndTimestamp, appliedInputGain);

    public bool TryWrite(
        ReadOnlySpan<float> interleavedSamples,
        int inputSampleRate,
        int inputChannels,
        long captureEndTimestamp,
        float appliedInputGain)
    {
        UtaPcmCaptureQueue? currentQueue;
        lock (sync)
        {
            if (state != UtaRecordingState.Recording)
                return false;

            if (sampleRate == 0 && channels == 0)
            {
                sampleRate = inputSampleRate;
                channels = inputChannels;
                Metadata.SampleRate = inputSampleRate;
                Metadata.Channels = inputChannels;
            }
            else if (inputSampleRate != sampleRate || inputChannels != channels)
            {
                setFaultNoIo(
                    UtaRecordingFaultReason.FormatChanged,
                    $"Microphone format changed from {sampleRate} Hz/{channels} ch to {inputSampleRate} Hz/{inputChannels} ch.");
                return false;
            }

            currentQueue = queue;
        }

        if (currentQueue == null)
            return false;

        bool accepted = currentQueue.TryWrite(
            interleavedSamples,
            inputSampleRate,
            inputChannels,
            captureEndTimestamp,
            appliedInputGain);

        if (!accepted)
        {
            setFaultNoIo(
                UtaRecordingFaultReason.QueueOverflow,
                "The bounded recording queue overflowed. Recording stopped rather than silently dropping samples.");
            currentQueue.Complete();
            publishProgress();
            return false;
        }

        Interlocked.Add(ref acceptedFrames, interleavedSamples.Length / inputChannels);
        return true;
    }

    public async Task<UtaRecordingMetadata> StopAsync()
    {
        Task? task;
        UtaPcmCaptureQueue? currentQueue;

        lock (sync)
        {
            if (state == UtaRecordingState.Ready)
                return Metadata;

            if (state is UtaRecordingState.Recording or UtaRecordingState.Paused)
                state = UtaRecordingState.Finalizing;

            currentQueue = queue;
            task = writerTask;
        }

        currentQueue?.Complete();
        publishProgress();

        if (task != null)
            await task.ConfigureAwait(false);

        lock (sync)
        {
            if (faultReason == UtaRecordingFaultReason.None)
            {
                state = UtaRecordingState.Completed;
                Metadata.Complete = true;
            }
            else
            {
                state = UtaRecordingState.Faulted;
                Metadata.Complete = false;
            }

            Metadata.FaultReason = faultReason;
            Metadata.FaultMessage = faultMessage;
            long finalFrame = Metadata.FrameCount;
            var completedSegments = new UtaRecordingSegment[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                UtaRecordingSegment segment = segments[i];
                long nextStart = i + 1 < segments.Count ? segments[i + 1].FileStartFrame : finalFrame;
                completedSegments[i] = segment with { FrameCount = Math.Max(0, nextStart - segment.FileStartFrame) };
            }
            Metadata.Segments = completedSegments;
        }

        publishProgress();
        return Metadata;
    }

    private async Task consumeAsync()
    {
        UtaPcmCaptureQueue currentQueue;
        UtaWavPcm16Writer? currentWriter;

        lock (sync)
        {
            currentQueue = queue ?? throw new InvalidOperationException("Recording queue was not initialised.");
            currentWriter = writer;
        }

        try
        {
            while (true)
            {
                UtaPcmCaptureBlock? block = await currentQueue.ReadAsync(CancellationToken.None).ConfigureAwait(false);
                if (block == null)
                    break;

                if (currentWriter == null)
                {
                    string path;
                    lock (sync)
                    {
                        path = filePath ?? throw new InvalidOperationException("Recording output path is unavailable.");
                        currentWriter = writer = new UtaWavPcm16Writer(path, block.SampleRate, block.Channels);
                    }
                }

                using (block)
                    currentWriter.Write(block.Samples);

                if ((currentWriter.FramesWritten & 0x3ffff) == 0)
                    publishProgress();
            }

            currentWriter?.Finalise();
        }
        catch (Exception ex)
        {
            setFaultNoIo(UtaRecordingFaultReason.DiskWriteFailed, ex.GetBaseException().Message);
        }
        finally
        {
            lock (sync)
            {
                Metadata.FrameCount = currentWriter?.FramesWritten ?? 0;
                Metadata.ClippedSamples = currentWriter?.ClippedSamples ?? 0;
            }

            if (currentWriter != null)
            {
                try
                {
                    currentWriter.Dispose();
                }
                catch (Exception ex)
                {
                    setFaultNoIo(UtaRecordingFaultReason.DiskWriteFailed, ex.GetBaseException().Message);
                }
            }

            await currentQueue.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void setFaultNoIo(UtaRecordingFaultReason reason, string message)
    {
        lock (sync)
        {
            if (faultReason != UtaRecordingFaultReason.None)
                return;

            faultReason = reason;
            faultMessage = message;
            state = UtaRecordingState.Faulted;
            Metadata.FaultReason = reason;
            Metadata.FaultMessage = message;
        }
    }

    private void publishProgress()
    {
        UtaPcmCaptureQueue? currentQueue;
        UtaRecordingProgress progress;

        lock (sync)
        {
            currentQueue = queue;
            progress = new UtaRecordingProgress(
                state,
                writer?.FramesWritten ?? 0,
                currentQueue?.QueuedFrames ?? 0,
                currentQueue?.RejectedBlocks ?? 0,
                filePath,
                faultReason,
                faultMessage);
        }

        ProgressChanged?.Invoke(progress);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
