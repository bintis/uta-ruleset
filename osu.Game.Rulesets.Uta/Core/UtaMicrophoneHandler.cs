// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;
using ManagedBass.Mix;
using osu.Framework.Bindables;
using osu.Framework.Input.Handlers;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Rulesets.Uta.Pitch;
using osu.Game.Rulesets.Uta.Recording;

namespace osu.Game.Rulesets.Uta.Core;

/// <summary>
/// Thin microphone source backed by the same BASS runtime shipped by osu!lazer.
/// BASS exposes the default PipeWire/PulseAudio input through its Linux backend.
/// </summary>
internal sealed class UtaMicrophoneHandler : InputHandler
{
    public event Action<UtaPitchFrame>? PitchDetected;

    public IUtaPcmCaptureSink? PcmCaptureSink { get; set; }

    public override bool IsActive => Bass.RecordingDeviceCount > 0;

    public BindableFloat MonitorVolume { get; } = new(0.35f)
    {
        MinValue = 0,
        MaxValue = 1,
        Precision = 0.01f,
    };

    public BindableFloat InputGain { get; } = new(1.5f)
    {
        MinValue = 0.5f,
        MaxValue = 3,
        Precision = 0.05f,
    };

    public Bindable<string> OutputDevice { get; } = new(string.Empty);

    public BindableBool DebugDiagnostics { get; } = new();

    public BindableFloat PitchSamplingInterval { get; } = new(10)
    {
        MinValue = 10,
        MaxValue = 40,
        Precision = 1,
    };

    private const int window_size = 2048;
    private readonly float[] samples = new float[window_size];
    private readonly int deviceIndex;
    private readonly UtaAudioRouter audioRouter;
    private int recordingStream;
    private int monitorStream;
    private int frequency;
    private int channels;
    private int sampleCount;
    private volatile float inputGain;
    private volatile float monitorVolume;
    private volatile float pitchSamplingInterval = 10;
    private readonly object pending_window_lock = new();
    private float[]? pendingWindow;
    private long pendingArrivalTimestamp;
    private bool detectionWorkerScheduled;
    private volatile bool recordingActive;
    private readonly SemaphoreSlim calibrationGate = new(1, 1);
    private CalibrationCapture? calibrationCapture;
    private CancellationTokenSource? activeCalibrationCancellation;
    private long diagnosticIntervalStart;
    private long diagnosticAnalysisTicks;
    private long diagnosticMaximumAnalysisTicks;
    private int diagnosticQueuedWindows;
    private int diagnosticProcessedWindows;
    private int diagnosticDroppedWindows;

    public UtaMicrophoneHandler(int deviceIndex, UtaAudioRouter audioRouter)
    {
        this.deviceIndex = deviceIndex;
        this.audioRouter = audioRouter;
    }

    public override bool Initialize(GameHost host)
    {
        DebugDiagnostics.BindValueChanged(_ => resetDiagnosticCounters(), true);
        PitchSamplingInterval.BindValueChanged(value => pitchSamplingInterval = value.NewValue, true);
        InputGain.BindValueChanged(value => inputGain = value.NewValue, true);
        MonitorVolume.BindValueChanged(value =>
        {
            monitorVolume = value.NewValue;
            if (monitorStream != 0)
                Bass.ChannelSetAttribute(monitorStream, ChannelAttribute.Volume, value.NewValue);
        }, true);
        OutputDevice.BindValueChanged(_ => attachMonitorToOutput(), true);
        Enabled.BindValueChanged(enabled =>
        {
            if (enabled.NewValue)
                start();
            else
                stop();
        }, true);
        return true;
    }

    private void start()
    {
        if (!Bass.RecordInit(deviceIndex))
        {
            Logger.Log($"Uta microphone unavailable: {Bass.LastError}", level: LogLevel.Error);
            return;
        }

        RecordInfo info = Bass.RecordingInfo;
        frequency = info.Frequency > 0 ? info.Frequency : 48000;
        channels = Math.Max(1, info.Channels);
        monitorStream = audioRouter.CreateMonitor(frequency, channels, OutputDevice.Value);
        if (monitorStream != 0)
        {
            Bass.ChannelSetAttribute(monitorStream, ChannelAttribute.Volume, monitorVolume);
        }

        recordingActive = true;
        recordingStream = Bass.RecordStart(frequency, channels, BassFlags.Float, 10 * channels, receive);

        if (recordingStream == 0)
        {
            Logger.Log($"Uta microphone could not start: {Bass.LastError}", level: LogLevel.Error);
            stop();
            return;
        }

        Bass.ChannelPlay(recordingStream);
    }

    private bool receive(int handle, IntPtr buffer, int length, IntPtr user)
    {
        int interleavedCount = length / sizeof(float);
        float[] interleaved = ArrayPool<float>.Shared.Rent(interleavedCount);
        try
        {
            Marshal.Copy(buffer, interleaved, 0, interleavedCount);

            float gain = inputGain;
            if (gain != 1)
            {
                for (int i = 0; i < interleavedCount; i++)
                    interleaved[i] *= gain;
            }

            // Recording is tapped after input gain and before monitor routing. The sink is
            // strictly non-blocking; file I/O happens on its bounded background consumer.
            if (Volatile.Read(ref calibrationCapture) == null)
            {
                PcmCaptureSink?.TryWrite(
                    interleaved.AsSpan(0, interleavedCount),
                    frequency,
                    channels,
                    Stopwatch.GetTimestamp(),
                    gain);
            }

            CalibrationCapture? capture = Volatile.Read(ref calibrationCapture);
            if (capture == null && monitorStream != 0 && monitorVolume > 0)
                Bass.StreamPutData(monitorStream, interleaved, length);

            for (int frame = 0; frame < interleavedCount / channels; frame++)
            {
                float mono = 0;
                for (int channel = 0; channel < channels; channel++)
                    mono += interleaved[frame * channels + channel];

                float monoSample = mono / channels;
                if (capture != null && capture.Count < capture.Samples.Length)
                    capture.Samples[capture.Count++] = monoSample;

                if (sampleCount < samples.Length)
                    samples[sampleCount++] = monoSample;

                if (sampleCount == samples.Length)
                {
                    if (capture == null)
                        queuePitchDetection();
                    int hopSamples = Math.Clamp((int)Math.Round(frequency * pitchSamplingInterval / 1000), 1, samples.Length);
                    int retainedSamples = samples.Length - hopSamples;
                    if (retainedSamples > 0)
                        Array.Copy(samples, samples.Length - retainedSamples, samples, 0, retainedSamples);
                    sampleCount = retainedSamples;
                }
            }

            if (capture != null && capture.Count == capture.Samples.Length
                                && ReferenceEquals(Interlocked.CompareExchange(ref calibrationCapture, null, capture), capture))
            {
                _ = Task.Run(() => capture.Completion.TrySetResult(analyseCalibration(capture)));
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(interleaved);
        }

        return true;
    }

    internal async Task<UtaLatencyCalibrationResult> CalibrateLatencyAsync(CancellationToken cancellationToken = default)
    {
        if (!recordingActive || recordingStream == 0 || frequency <= 0)
            return new UtaLatencyCalibrationResult(false, 0, 0, 0, "Microphone input is not active.");

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool gateEntered;
        try
        {
            gateEntered = await calibrationGate.WaitAsync(0, linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new UtaLatencyCalibrationResult(false, 0, 0, 0, "Latency measurement was cancelled.");
        }

        if (!gateEntered)
            return new UtaLatencyCalibrationResult(false, 0, 0, 0, "A latency measurement is already running.");

        Interlocked.Exchange(ref activeCalibrationCancellation, linkedCancellation);

        try
        {
            var latencies = new List<double>(3);
            double confidenceSum = 0;

            for (int round = 0; round < 3; round++)
            {
                linkedCancellation.Token.ThrowIfCancellationRequested();
                UtaLatencyMeasurement measurement = await measureLatencyOnce(linkedCancellation.Token).ConfigureAwait(false);
                if (measurement.Success)
                {
                    latencies.Add(measurement.LatencyMilliseconds);
                    confidenceSum += measurement.Confidence;
                }

                if (round < 2)
                    await Task.Delay(120, linkedCancellation.Token).ConfigureAwait(false);
            }

            if (latencies.Count < 2)
            {
                return new UtaLatencyCalibrationResult(
                    false, 0, 0, latencies.Count == 0 ? 0 : confidenceSum / latencies.Count,
                    "Could not hear the probe clearly. Use speakers or a loopback path, then retry.");
            }

            latencies.Sort();
            double median = latencies[latencies.Count / 2];
            double spread = latencies[^1] - latencies[0];
            double confidence = confidenceSum / latencies.Count;
            if (spread > 80)
            {
                return new UtaLatencyCalibrationResult(
                    false, median, spread, confidence,
                    $"Measurements were unstable ({spread:0} ms spread). Reduce noise and retry.");
            }

            return new UtaLatencyCalibrationResult(
                true, Math.Clamp(median, 0, 1000), spread, confidence,
                $"Measured {median:0} ms ({spread:0} ms spread).");
        }
        catch (OperationCanceledException)
        {
            return new UtaLatencyCalibrationResult(false, 0, 0, 0, "Latency measurement was cancelled.");
        }
        finally
        {
            Interlocked.CompareExchange(ref activeCalibrationCancellation, null, linkedCancellation);
            calibrationGate.Release();
        }
    }

    private async Task<UtaLatencyMeasurement> measureLatencyOnce(CancellationToken cancellationToken)
    {
        CalibrationProbe probe = createCalibrationProbe(frequency);
        var capture = new CalibrationCapture(
            new float[Math.Max(frequency * 5 / 4, probe.Output.Length + frequency / 2)],
            probe.Reference,
            probe.PrefixSamples,
            probe.Downsample,
            frequency);
        int calibrationStream = 0;

        if (Interlocked.CompareExchange(ref calibrationCapture, capture, null) != null)
            return default;

        try
        {
            calibrationStream = audioRouter.CreateMonitor(frequency, 1, OutputDevice.Value);
            if (calibrationStream == 0)
                return default;

            if (Bass.StreamPutData(calibrationStream, probe.Output, probe.Output.Length * sizeof(float)) < 0)
                return default;

            return await capture.Completion.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return default;
        }
        finally
        {
            Interlocked.CompareExchange(ref calibrationCapture, null, capture);
            if (calibrationStream != 0)
            {
                BassMix.MixerRemoveChannel(calibrationStream);
                Bass.StreamFree(calibrationStream);
            }
            sampleCount = 0;
        }
    }

    private static CalibrationProbe createCalibrationProbe(int sampleRate)
    {
        const int sequence_length = 255;
        const int chip_samples = 32;
        const int downsample = 16;
        int prefixSamples = sampleRate / 5;
        int probeSamples = sequence_length * chip_samples;
        int tailSamples = sampleRate * 3 / 5;
        var output = new float[prefixSamples + probeSamples + tailSamples];
        uint lfsr = 0xff;

        for (int chip = 0; chip < sequence_length; chip++)
        {
            float value = (lfsr & 1) == 0 ? -0.12f : 0.12f;
            int start = prefixSamples + chip * chip_samples;
            for (int i = 0; i < chip_samples; i++)
                output[start + i] = value;

            uint feedback = ((lfsr >> 0) ^ (lfsr >> 2) ^ (lfsr >> 3) ^ (lfsr >> 4)) & 1;
            lfsr = (lfsr >> 1) | (feedback << 7);
        }

        int referenceLength = probeSamples / downsample;
        var reference = new float[referenceLength];
        for (int i = 0; i < referenceLength; i++)
        {
            float sum = 0;
            int start = prefixSamples + i * downsample;
            for (int j = 0; j < downsample; j++)
                sum += output[start + j];
            reference[i] = sum / downsample;
        }

        return new CalibrationProbe(output, reference, prefixSamples, downsample);
    }

    private static UtaLatencyMeasurement analyseCalibration(CalibrationCapture capture)
    {
        int downsampledLength = capture.Count / capture.Downsample;
        if (downsampledLength <= capture.Reference.Length)
            return default;

        var signal = new double[downsampledLength];
        double signalMean = 0;
        for (int i = 0; i < signal.Length; i++)
        {
            double sum = 0;
            int start = i * capture.Downsample;
            for (int j = 0; j < capture.Downsample; j++)
                sum += capture.Samples[start + j];
            signal[i] = sum / capture.Downsample;
            signalMean += signal[i];
        }
        signalMean /= signal.Length;
        for (int i = 0; i < signal.Length; i++)
            signal[i] -= signalMean;

        var reference = new double[capture.Reference.Length];
        double referenceMean = 0;
        foreach (float sample in capture.Reference)
            referenceMean += sample;
        referenceMean /= capture.Reference.Length;

        double referenceEnergy = 0;
        for (int i = 0; i < reference.Length; i++)
        {
            reference[i] = capture.Reference[i] - referenceMean;
            referenceEnergy += reference[i] * reference[i];
        }

        int lastLag = signal.Length - reference.Length;
        double windowEnergy = 0;
        for (int i = 0; i < reference.Length; i++)
            windowEnergy += signal[i] * signal[i];

        double bestScore = 0;
        int bestLag = 0;
        for (int lag = 0; lag <= lastLag; lag++)
        {
            double product = 0;
            for (int i = 0; i < reference.Length; i++)
                product += signal[lag + i] * reference[i];

            double score = Math.Abs(product) / Math.Max(double.Epsilon, Math.Sqrt(referenceEnergy * windowEnergy));
            if (score > bestScore)
            {
                bestScore = score;
                bestLag = lag;
            }

            if (lag < lastLag)
            {
                windowEnergy -= signal[lag] * signal[lag];
                windowEnergy += signal[lag + reference.Length] * signal[lag + reference.Length];
            }
        }

        double latencyMilliseconds = (bestLag * capture.Downsample - capture.PrefixSamples) * 1000.0 / capture.SampleRate;
        bool success = bestScore >= 0.12 && latencyMilliseconds is >= 0 and <= 1000;
        return new UtaLatencyMeasurement(success, latencyMilliseconds, bestScore);
    }

    private void queuePitchDetection()
    {
        Interlocked.Increment(ref diagnosticQueuedWindows);
        float[] window = ArrayPool<float>.Shared.Rent(window_size);
        samples.CopyTo(window, 0);
        float[]? replaced;
        bool scheduleWorker = false;

        lock (pending_window_lock)
        {
            replaced = pendingWindow;
            pendingWindow = window;
            pendingArrivalTimestamp = Stopwatch.GetTimestamp();
            if (!detectionWorkerScheduled)
            {
                detectionWorkerScheduled = true;
                scheduleWorker = true;
            }
        }

        if (replaced != null)
        {
            Interlocked.Increment(ref diagnosticDroppedWindows);
            ArrayPool<float>.Shared.Return(replaced);
        }
        if (scheduleWorker)
            ThreadPool.UnsafeQueueUserWorkItem(static state => ((UtaMicrophoneHandler)state!).processPitchWindows(), this);
    }

    private void processPitchWindows()
    {
        while (true)
        {
            float[]? window;
            long arrivalTimestamp;

            lock (pending_window_lock)
            {
                window = pendingWindow;
                arrivalTimestamp = pendingArrivalTimestamp;
                pendingWindow = null;
                if (window == null)
                {
                    detectionWorkerScheduled = false;
                    return;
                }
            }

            UtaPitchAnalysis analysis;
            long analysisStart = Stopwatch.GetTimestamp();
            try
            {
                analysis = UtaPitchDetector.Analyse(window.AsSpan(0, window_size), frequency);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(window);
            }
            long analysisTicks = Stopwatch.GetTimestamp() - analysisStart;
            Interlocked.Increment(ref diagnosticProcessedWindows);
            Interlocked.Add(ref diagnosticAnalysisTicks, analysisTicks);
            updateMaximum(ref diagnosticMaximumAnalysisTicks, analysisTicks);
            reportDiagnostics();

            if (recordingActive)
            {
                PitchDetected?.Invoke(new UtaPitchFrame(
                    analysis.Hertz,
                    analysis.Clarity,
                    analysis.Rms,
                    arrivalTimestamp,
                    window_size * 1000.0 / frequency));
            }
        }
    }

    private void reportDiagnostics()
    {
        if (!DebugDiagnostics.Value)
            return;

        long now = Stopwatch.GetTimestamp();
        long start = Volatile.Read(ref diagnosticIntervalStart);
        if (Stopwatch.GetElapsedTime(start, now).TotalSeconds < 5
            || Interlocked.CompareExchange(ref diagnosticIntervalStart, now, start) != start)
            return;

        int queued = Interlocked.Exchange(ref diagnosticQueuedWindows, 0);
        int processed = Interlocked.Exchange(ref diagnosticProcessedWindows, 0);
        int dropped = Interlocked.Exchange(ref diagnosticDroppedWindows, 0);
        long totalTicks = Interlocked.Exchange(ref diagnosticAnalysisTicks, 0);
        long maximumTicks = Interlocked.Exchange(ref diagnosticMaximumAnalysisTicks, 0);
        double averageMs = processed == 0 ? 0 : totalTicks * 1000.0 / Stopwatch.Frequency / processed;
        double maximumMs = maximumTicks * 1000.0 / Stopwatch.Frequency;
        Logger.Log(
            $"Uta debug microphone: queued={queued} processed={processed} dropped={dropped} " +
            $"analysis-avg={averageMs:0.00}ms analysis-max={maximumMs:0.00}ms " +
            $"frequency={frequency}Hz channels={channels} interval={pitchSamplingInterval:0}ms active={recordingActive}");
    }

    private void resetDiagnosticCounters()
    {
        diagnosticIntervalStart = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref diagnosticAnalysisTicks, 0);
        Interlocked.Exchange(ref diagnosticMaximumAnalysisTicks, 0);
        Interlocked.Exchange(ref diagnosticQueuedWindows, 0);
        Interlocked.Exchange(ref diagnosticProcessedWindows, 0);
        Interlocked.Exchange(ref diagnosticDroppedWindows, 0);
    }

    private static void updateMaximum(ref long target, long value)
    {
        long current;
        while (value > (current = Volatile.Read(ref target))
               && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private void stop()
    {
        recordingActive = false;
        Volatile.Read(ref activeCalibrationCancellation)?.Cancel();
        if (recordingStream != 0)
            Bass.ChannelPause(recordingStream);
        Bass.RecordFree();
        recordingStream = 0;

        if (monitorStream != 0)
        {
            BassMix.MixerRemoveChannel(monitorStream);
            Bass.StreamFree(monitorStream);
            monitorStream = 0;
        }
        float[]? pending;
        lock (pending_window_lock)
        {
            pending = pendingWindow;
            pendingWindow = null;
        }
        if (pending != null)
            ArrayPool<float>.Shared.Return(pending);
        sampleCount = 0;
        PitchDetected?.Invoke(new UtaPitchFrame(null, 0, 0, Stopwatch.GetTimestamp(), 0));
    }

    private void attachMonitorToOutput()
    {
        if (monitorStream == 0)
            return;

        audioRouter.Route(monitorStream, OutputDevice.Value, false);
    }

    protected override void Dispose(bool isDisposing)
    {
        stop();
        DebugDiagnostics.UnbindAll();
        PitchSamplingInterval.UnbindAll();
        base.Dispose(isDisposing);
    }

    private sealed class CalibrationCapture
    {
        public readonly float[] Samples;
        public readonly float[] Reference;
        public readonly int PrefixSamples;
        public readonly int Downsample;
        public readonly int SampleRate;
        public readonly TaskCompletionSource<UtaLatencyMeasurement> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count;

        public CalibrationCapture(float[] samples, float[] reference, int prefixSamples, int downsample, int sampleRate)
        {
            Samples = samples;
            Reference = reference;
            PrefixSamples = prefixSamples;
            Downsample = downsample;
            SampleRate = sampleRate;
        }
    }

    private readonly record struct CalibrationProbe(float[] Output, float[] Reference, int PrefixSamples, int Downsample);
    private readonly record struct UtaLatencyMeasurement(bool Success, double LatencyMilliseconds, double Confidence);
}

internal readonly record struct UtaLatencyCalibrationResult(
    bool Success,
    double LatencyMilliseconds,
    double SpreadMilliseconds,
    double Confidence,
    string Message);
