// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
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
    private const int monitor_target_buffer_ms = 60;
    private const int monitor_clock_adjustment_interval = 10;
    private const float monitor_max_clock_correction = 0.005f;
    private readonly float[] samples = new float[window_size];
    private float[] interleavedBuffer = Array.Empty<float>();
    private readonly int deviceIndex;
    private readonly UtaAudioRouter audioRouter;
    private int recordingStream;
    private int monitorStream;
    private int monitorClockAdjustmentCounter;
    private int frequency;
    private int channels;
    private int sampleWriteIndex;
    private int samplesFilled;
    private int samplesUntilDetection;
    private volatile float inputGain;
    private volatile float monitorVolume;
    private volatile float pitchSamplingInterval = 10;
    private readonly object pending_window_lock = new();
    private float[] pendingWindow = new float[window_size];
    private float[] workerWindow = new float[window_size];
    private bool hasPendingWindow;
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
    private int diagnosticInputPeakMilli;
    private volatile bool debugDiagnosticsEnabled;
    private string captureDeviceName = string.Empty;

    public UtaMicrophoneHandler(int deviceIndex, UtaAudioRouter audioRouter)
    {
        this.deviceIndex = deviceIndex;
        this.audioRouter = audioRouter;
    }

    public override bool Initialize(GameHost host)
    {
        DebugDiagnostics.BindValueChanged(value =>
        {
            debugDiagnosticsEnabled = value.NewValue;
            resetDiagnosticCounters();
        }, true);
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
        try
        {
            startRecording();
        }
        catch (Exception ex)
        {
            Logger.Log($"Uta microphone unavailable: {ex.Message}", level: LogLevel.Error);
            try
            {
                Bass.RecordFree();
            }
            catch (Exception)
            {
            }
        }
    }

    private void startRecording()
    {
        if (!Bass.RecordInit(deviceIndex) && Bass.LastError != Errors.Already)
        {
            Logger.Log($"Uta microphone unavailable: {Bass.LastError}", level: LogLevel.Error);
            return;
        }

        int actual = deviceIndex >= 0 ? deviceIndex : Bass.CurrentRecordingDevice;
        if (actual < 0 || !Bass.RecordGetDeviceInfo(actual, out DeviceInfo captureInfo))
        {
            Logger.Log($"Uta microphone device info unavailable: {Bass.LastError} index={deviceIndex} actual={actual}", level: LogLevel.Error);
            Bass.RecordFree();
            return;
        }

        captureDeviceName = captureInfo.Name ?? string.Empty;

        RecordInfo info = Bass.RecordingInfo;
        frequency = info.Frequency > 0 ? info.Frequency : 48000;
        channels = Math.Max(1, info.Channels);

        // BASS is configured for a 10ms recording period. Reserve a generous 20ms buffer
        // before the callback starts so normal capture never rents/returns a managed array.
        int expectedInterleavedSamples = Math.Max(1024, checked(frequency * channels / 50));
        if (interleavedBuffer.Length < expectedInterleavedSamples)
            interleavedBuffer = new float[expectedInterleavedSamples];

        resetSampleWindow();
        monitorStream = audioRouter.CreateMonitor(frequency, channels, OutputDevice.Value);
        if (monitorStream != 0)
        {
            Bass.ChannelSetAttribute(monitorStream, ChannelAttribute.Volume, monitorVolume);
            primeMonitorBuffer(monitorStream);
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

        if (debugDiagnosticsEnabled)
        {
            ChannelInfo recordInfo = Bass.ChannelGetInfo(recordingStream);
            Logger.Log($"Uta debug microphone: started deviceIndex={deviceIndex} name='{captureDeviceName}' frequency={frequency}Hz channels={channels} "
                       + $"record-flags={recordInfo.Flags} monitorStream={(monitorStream != 0 ? "attached" : "none")} monitor-volume={monitorVolume:P0} "
                       + $"captureSink={(PcmCaptureSink != null ? "attached" : "none")}");
        }
    }

    private bool receive(int handle, IntPtr buffer, int length, IntPtr user)
    {
        int interleavedCount = length / sizeof(float);
        ensureInterleavedCapacity(interleavedCount);
        float[] interleaved = interleavedBuffer;
        Marshal.Copy(buffer, interleaved, 0, interleavedCount);

        float gain = inputGain;
        if (gain != 1)
        {
            for (int i = 0; i < interleavedCount; i++)
                interleaved[i] = Math.Clamp(interleaved[i] * gain, -1, 1);
        }

        if (debugDiagnosticsEnabled)
        {
            float peak = 0;
            for (int i = 0; i < interleavedCount; i++)
            {
                float absolute = Math.Abs(interleaved[i]);
                if (absolute > peak)
                    peak = absolute;
            }

            updateMaximum(ref diagnosticInputPeakMilli, (int)(peak * 1000));
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
        if (capture == null && monitorStream != 0)
            pushMonitorAudio(interleaved, length);

        int frameCount = interleavedCount / channels;
        for (int frame = 0; frame < frameCount; frame++)
        {
            int frameOffset = frame * channels;
            float monoSample;
            if (channels == 1)
            {
                // The overwhelmingly common microphone format. Avoid the inner channel loop
                // and divide while preserving the exact multi-channel averaging path below.
                monoSample = interleaved[frameOffset];
            }
            else
            {
                float mono = 0;
                for (int channel = 0; channel < channels; channel++)
                    mono += interleaved[frameOffset + channel];
                monoSample = mono / channels;
            }
            if (capture != null && capture.Count < capture.Samples.Length)
                capture.Samples[capture.Count++] = monoSample;

            samples[sampleWriteIndex] = monoSample;
            sampleWriteIndex++;
            if (sampleWriteIndex == window_size)
                sampleWriteIndex = 0;

            if (samplesFilled < window_size)
            {
                samplesFilled++;
                if (samplesFilled < window_size)
                    continue;
            }

            if (samplesUntilDetection > 0)
                samplesUntilDetection--;

            if (samplesUntilDetection == 0)
            {
                if (capture == null)
                    queuePitchDetection();

                samplesUntilDetection = currentHopSamples();
            }
        }

        if (capture != null && capture.Count == capture.Samples.Length
                            && ReferenceEquals(Interlocked.CompareExchange(ref calibrationCapture, null, capture), capture))
        {
            _ = Task.Run(() => capture.Completion.TrySetResult(analyseCalibration(capture)));
        }

        return true;
    }

    private void primeMonitorBuffer(int stream)
    {
        int targetBytes = getMonitorTargetBufferBytes();
        Bass.StreamPutData(stream, new float[targetBytes / sizeof(float)], targetBytes);
        monitorClockAdjustmentCounter = 0;
    }

    private void pushMonitorAudio(float[] source, int byteLength)
    {
        int stream = monitorStream;

        if (++monitorClockAdjustmentCounter >= monitor_clock_adjustment_interval)
        {
            monitorClockAdjustmentCounter = 0;

            int targetBytes = getMonitorTargetBufferBytes();
            int queuedBytes = Bass.ChannelGetData(stream, IntPtr.Zero, (int)DataFlags.Available);

            if (queuedBytes >= 0)
            {
                if (queuedBytes < targetBytes / 3)
                {
                    int missingBytes = targetBytes - queuedBytes;
                    Bass.StreamPutData(stream, new float[missingBytes / sizeof(float)], missingBytes);
                    queuedBytes = targetBytes;
                }

                float bufferError = (queuedBytes - targetBytes) / (float)targetBytes;
                float correction = Math.Clamp(bufferError * 0.01f, -monitor_max_clock_correction, monitor_max_clock_correction);
                Bass.ChannelSetAttribute(stream, ChannelAttribute.Frequency, frequency * (1 + correction));
            }
        }

        Bass.StreamPutData(stream, source, byteLength);
    }

    private int getMonitorTargetBufferBytes()
        => frequency * channels * sizeof(float) * monitor_target_buffer_ms / 1000;

    private void ensureInterleavedCapacity(int required)
    {
        if (required <= interleavedBuffer.Length)
            return;

        int capacity = Math.Max(required, Math.Max(1024, interleavedBuffer.Length * 2));
        interleavedBuffer = new float[capacity];
    }

    private int currentHopSamples()
        => Math.Clamp((int)Math.Round(frequency * pitchSamplingInterval / 1000), 1, window_size);

    private void resetSampleWindow()
    {
        sampleWriteIndex = 0;
        samplesFilled = 0;
        samplesUntilDetection = 0;
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
            resetSampleWindow();
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
        if (debugDiagnosticsEnabled)
            Interlocked.Increment(ref diagnosticQueuedWindows);
        bool scheduleWorker = false;

        lock (pending_window_lock)
        {
            if (hasPendingWindow && debugDiagnosticsEnabled)
                Interlocked.Increment(ref diagnosticDroppedWindows);

            // sampleWriteIndex points at the oldest sample once the ring is full.
            int tailCount = window_size - sampleWriteIndex;
            samples.AsSpan(sampleWriteIndex, tailCount).CopyTo(pendingWindow);
            if (sampleWriteIndex > 0)
                samples.AsSpan(0, sampleWriteIndex).CopyTo(pendingWindow.AsSpan(tailCount));

            hasPendingWindow = true;
            pendingArrivalTimestamp = Stopwatch.GetTimestamp();
            if (!detectionWorkerScheduled)
            {
                detectionWorkerScheduled = true;
                scheduleWorker = true;
            }
        }

        if (scheduleWorker)
            ThreadPool.UnsafeQueueUserWorkItem(static state => ((UtaMicrophoneHandler)state!).processPitchWindows(), this);
    }

    private void processPitchWindows()
    {
        while (true)
        {
            long arrivalTimestamp;

            lock (pending_window_lock)
            {
                if (!hasPendingWindow)
                {
                    detectionWorkerScheduled = false;
                    return;
                }

                // Fixed double buffer: the worker owns workerWindow outside the lock while
                // the callback is free to overwrite pendingWindow with the newest analysis window.
                (pendingWindow, workerWindow) = (workerWindow, pendingWindow);
                hasPendingWindow = false;
                arrivalTimestamp = pendingArrivalTimestamp;
            }

            UtaPitchAnalysis analysis;
            if (debugDiagnosticsEnabled)
            {
                long analysisStart = Stopwatch.GetTimestamp();
                analysis = UtaPitchDetector.Analyse(workerWindow, frequency);
                long analysisTicks = Stopwatch.GetTimestamp() - analysisStart;
                Interlocked.Increment(ref diagnosticProcessedWindows);
                Interlocked.Add(ref diagnosticAnalysisTicks, analysisTicks);
                updateMaximum(ref diagnosticMaximumAnalysisTicks, analysisTicks);
                reportDiagnostics();
            }
            else
            {
                analysis = UtaPitchDetector.Analyse(workerWindow, frequency);
            }

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
        if (!debugDiagnosticsEnabled)
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
        int inputPeakMilli = Interlocked.Exchange(ref diagnosticInputPeakMilli, 0);
        double averageMs = processed == 0 ? 0 : totalTicks * 1000.0 / Stopwatch.Frequency / processed;
        double maximumMs = maximumTicks * 1000.0 / Stopwatch.Frequency;
        Logger.Log(
            $"Uta debug microphone: queued={queued} processed={processed} dropped={dropped} " +
            $"analysis-avg={averageMs:0.00}ms analysis-max={maximumMs:0.00}ms " +
            $"input-peak={inputPeakMilli / 1000f:0.000} monitor-volume={monitorVolume:P0} " +
            $"frequency={frequency}Hz channels={channels} interval={pitchSamplingInterval:0}ms active={recordingActive}");
    }

    private void resetDiagnosticCounters()
    {
        diagnosticIntervalStart = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
        Interlocked.Exchange(ref diagnosticAnalysisTicks, 0);
        Interlocked.Exchange(ref diagnosticMaximumAnalysisTicks, 0);
        Interlocked.Exchange(ref diagnosticQueuedWindows, 0);
        Interlocked.Exchange(ref diagnosticProcessedWindows, 0);
        Interlocked.Exchange(ref diagnosticDroppedWindows, 0);
        Interlocked.Exchange(ref diagnosticInputPeakMilli, 0);
    }

    private static void updateMaximum(ref long target, long value)
    {
        long current;
        while (value > (current = Volatile.Read(ref target))
               && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private static void updateMaximum(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target))
               && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private void stop()
    {
        if (debugDiagnosticsEnabled && recordingActive)
            Logger.Log($"Uta debug microphone: stopped deviceIndex={deviceIndex}");

        recordingActive = false;
        Volatile.Read(ref activeCalibrationCancellation)?.Cancel();
        if (recordingStream != 0)
            Bass.ChannelPause(recordingStream);
        Bass.RecordFree();
        recordingStream = 0;

        if (monitorStream != 0)
        {
            audioRouter.UnprotectSource(monitorStream);
            BassMix.MixerRemoveChannel(monitorStream);
            Bass.StreamFree(monitorStream);
            monitorStream = 0;
        }

        lock (pending_window_lock)
            hasPendingWindow = false;

        resetSampleWindow();
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
