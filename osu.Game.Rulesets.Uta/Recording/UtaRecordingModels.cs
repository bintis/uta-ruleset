// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.Uta.Recording;

public enum UtaRecordingState
{
    Disabled,
    Ready,
    Recording,
    Paused,
    Finalizing,
    Completed,
    Faulted,
}

public enum UtaRecordingSegmentReason
{
    GameplayStart,
    Resume,
    PlaybackRateChanged,
    ForwardSeek,
    BackwardSeek,
    PhraseRetry,
    PhraseLoop,
    PreviousPhrase,
    NextPhrase,
    DeviceRestart,
}

public enum UtaRecordingFaultReason
{
    None,
    QueueOverflow,
    FormatChanged,
    DiskWriteFailed,
    DeviceDisconnected,
    Cancelled,
}

public sealed class UtaRecordingMetadata
{
    public Guid TakeId { get; set; } = Guid.NewGuid();
    public string Container { get; set; } = "wav";
    public string SampleFormat { get; set; } = "pcm_s16le";
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public long FrameCount { get; set; }
    public double CalibratedLatencyMilliseconds { get; set; }
    public double InputGain { get; set; } = 1;
    public int TransposeSemitones { get; set; }
    public bool OctaveFold { get; set; }
    public string InputDevice { get; set; } = string.Empty;
    public string MonitorOutputDevice { get; set; } = string.Empty;
    public string SignalStage { get; set; } = "post_input_gain_pre_monitor";
    public bool Complete { get; set; }
    public UtaRecordingFaultReason FaultReason { get; set; }
    public string? FaultMessage { get; set; }
    public long ClippedSamples { get; set; }
    public IReadOnlyList<UtaRecordingSegment> Segments { get; set; } = Array.Empty<UtaRecordingSegment>();
}

public readonly record struct UtaRecordingSegment(
    long FileStartFrame,
    long FrameCount,
    long SongStartTimeMicroseconds,
    int PlaybackRateMillionths,
    int TimelineEpoch,
    UtaRecordingSegmentReason Reason);

public readonly record struct UtaRecordingProgress(
    UtaRecordingState State,
    long RecordedFrames,
    long QueuedFrames,
    long RejectedBlocks,
    string? FilePath,
    UtaRecordingFaultReason FaultReason,
    string? ErrorMessage);
