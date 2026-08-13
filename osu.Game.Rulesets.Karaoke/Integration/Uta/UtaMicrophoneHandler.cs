// Copyright (c) karaoke.dev and bintis. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ManagedBass;
using osu.Framework.Bindables;
using osu.Framework.Input;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.StateChanges;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Rulesets.Karaoke.Scoring.Uta;

namespace osu.Game.Rulesets.Karaoke.Integration.Uta;

/// <summary>
/// Low-latency microphone capture owned by Uta. It keeps capture alive while
/// gameplay input is paused and exposes independent monitoring and input gain.
/// </summary>
internal class UtaMicrophoneHandler : InputHandler
{
    public event Action<Voice>? VoiceDetected;

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

    private const int pitch_window_size = 2048;
    private const int retained_sample_count = 4096;
    private const int pitch_hold_duration = 250;

    private readonly int deviceIndex;
    private readonly float[] sampleRing = new float[retained_sample_count];
    private readonly float[] pitchWindow = new float[pitch_window_size];

    private int recordingStream;
    private int monitorStream;
    private int recordingFrequency;
    private int recordingChannels;
    private int sampleWritePosition;
    private int availableSampleCount;
    private volatile float monitorVolume;
    private volatile float inputGain;
    private long lastPitchDiagnosticTimestamp;
    private long pitchHoldUntil;
    private float heldPitch;
    private int voicedWindowCount;
    private Voice lastDetectedVoice;

    public UtaMicrophoneHandler(int deviceIndex)
    {
        this.deviceIndex = deviceIndex;
    }

    public override bool Initialize(GameHost host)
    {
        MonitorVolume.BindValueChanged(value =>
        {
            monitorVolume = value.NewValue;
            if (monitorStream != 0)
                Bass.ChannelSetAttribute(monitorStream, ChannelAttribute.Volume, value.NewValue);
        }, true);

        InputGain.BindValueChanged(value => inputGain = value.NewValue, true);
        Enabled.BindValueChanged(value =>
        {
            if (value.NewValue)
                startCapture();
            else
                stopCapture();
        }, true);

        return true;
    }

    public override void Reset()
    {
        MonitorVolume.SetDefault();
        InputGain.SetDefault();
        base.Reset();
    }

    private void startCapture()
    {
        if (!Bass.RecordInit(deviceIndex))
        {
            Logger.Log($"Could not initialise microphone device {deviceIndex}: {Bass.LastError}", level: LogLevel.Error);
            return;
        }

        RecordInfo info = Bass.RecordingInfo;
        recordingFrequency = info.Frequency > 0 ? info.Frequency : 48000;
        recordingChannels = info.Channels > 0 ? info.Channels : 1;
        int period = 10 * recordingChannels;

        int currentDevice = Bass.CurrentRecordingDevice;
        string deviceName = currentDevice >= 0 ? Bass.RecordGetDeviceInfo(currentDevice).Name : "system default";
        Logger.Log($"Microphone recording initialised: {deviceName} (requested {deviceIndex}, active {currentDevice}), " +
                   $"{recordingFrequency} Hz, {recordingChannels} channel(s)");

        sampleWritePosition = 0;
        availableSampleCount = 0;
        lastPitchDiagnosticTimestamp = 0;
        pitchHoldUntil = 0;
        heldPitch = 0;
        voicedWindowCount = 0;
        lastDetectedVoice = default;

        monitorStream = Bass.CreateStream(recordingFrequency, recordingChannels, BassFlags.Float, StreamProcedureType.Push);
        if (monitorStream != 0)
        {
            Bass.ChannelSetAttribute(monitorStream, ChannelAttribute.Volume, monitorVolume);
            Bass.ChannelPlay(monitorStream);
        }

        recordingStream = Bass.RecordStart(recordingFrequency, recordingChannels, BassFlags.RecordPause | BassFlags.Float, period, processAudio);
        if (recordingStream == 0)
        {
            Logger.Log($"Could not start microphone recording: {Bass.LastError}", level: LogLevel.Error);
            stopCapture();
            return;
        }

        Bass.ChannelPlay(recordingStream);
    }

    private void stopCapture()
    {
        if (recordingStream != 0)
            Bass.ChannelPause(recordingStream);

        Bass.RecordFree();
        recordingStream = 0;

        if (monitorStream != 0)
        {
            Bass.StreamFree(monitorStream);
            monitorStream = 0;
        }
    }

    private bool processAudio(int handle, IntPtr buffer, int length, IntPtr user)
    {
        int sampleCount = length / sizeof(float);
        var samples = new float[sampleCount];
        Marshal.Copy(buffer, samples, 0, sampleCount);

        float gain = inputGain;
        if (gain != 1)
        {
            for (int i = 0; i < samples.Length; i++)
                samples[i] *= gain;
        }

        int activeMonitorStream = monitorStream;
        if (activeMonitorStream != 0 && monitorVolume > 0)
        {
            Bass.StreamPutData(activeMonitorStream, samples, length);
            Bass.ChannelPlay(activeMonitorStream);
        }

        appendMonoSamples(samples);
        if (availableSampleCount < pitch_window_size)
            return true;

        copyLatestPitchWindow();
        float rms = calculateRms(pitchWindow);
        float rawPitch = (float)(UtaPitchDetector.Detect(pitchWindow, recordingFrequency) ?? 0);
        long now = Environment.TickCount64;
        float pitch;

        if (rawPitch > 0)
        {
            voicedWindowCount++;
            if (heldPitch > 0 || voicedWindowCount >= 2)
            {
                heldPitch = rawPitch;
                pitchHoldUntil = now + pitch_hold_duration;
                pitch = rawPitch;
            }
            else
            {
                pitch = 0;
            }
        }
        else if (heldPitch > 0 && now < pitchHoldUntil)
        {
            voicedWindowCount = 0;
            pitch = heldPitch;
        }
        else
        {
            voicedWindowCount = 0;
            heldPitch = 0;
            pitch = 0;
        }

        if (now - lastPitchDiagnosticTimestamp >= 2000)
        {
            lastPitchDiagnosticTimestamp = now;
            Logger.Log($"Microphone pitch diagnostics: rms={rms:F5}, raw={rawPitch:F1} Hz, active={pitch:F1} Hz, gain={gain:F2}x");
        }

        dispatch(new Voice(pitch, calculateDecibel(rms)));
        return true;
    }

    private void appendMonoSamples(ReadOnlySpan<float> interleaved)
    {
        int channels = Math.Max(recordingChannels, 1);
        int frameCount = interleaved.Length / channels;

        for (int frame = 0; frame < frameCount; frame++)
        {
            float mono = 0;
            int start = frame * channels;
            for (int channel = 0; channel < channels; channel++)
                mono += interleaved[start + channel];
            mono /= channels;

            sampleRing[sampleWritePosition] = mono;
            sampleWritePosition = (sampleWritePosition + 1) % retained_sample_count;
            availableSampleCount = Math.Min(availableSampleCount + 1, retained_sample_count);
        }
    }

    private void copyLatestPitchWindow()
    {
        int start = (sampleWritePosition - pitch_window_size + retained_sample_count) % retained_sample_count;
        int firstLength = Math.Min(pitch_window_size, retained_sample_count - start);
        Array.Copy(sampleRing, start, pitchWindow, 0, firstLength);
        if (firstLength < pitch_window_size)
            Array.Copy(sampleRing, 0, pitchWindow, firstLength, pitch_window_size - firstLength);
    }

    private void dispatch(Voice voice)
    {
        if (lastDetectedVoice != voice)
        {
            lastDetectedVoice = voice;
            VoiceDetected?.Invoke(voice);
        }

        PendingInputs.Enqueue(new MicrophoneInput { Voice = voice });
    }

    private static float calculateRms(IReadOnlyCollection<float> samples)
    {
        double sum = 0;
        foreach (float sample in samples)
            sum += sample * sample;
        return (float)Math.Sqrt(sum / samples.Count);
    }

    private static float calculateDecibel(float rms)
        => rms > 0 ? 20 * MathF.Log10(rms) + 50 : float.NegativeInfinity;
}
