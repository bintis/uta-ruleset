// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Mix;
using osu.Framework.Bindables;
using osu.Framework.Input.Handlers;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Rulesets.Uta.Pitch;

namespace osu.Game.Rulesets.Uta.Core;

/// <summary>
/// Thin microphone source backed by the same BASS runtime shipped by osu!lazer.
/// BASS exposes the default PipeWire/PulseAudio input through its Linux backend.
/// </summary>
internal sealed class UtaMicrophoneHandler : InputHandler
{
    public event Action<double?>? PitchDetected;

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

    public UtaMicrophoneHandler(int deviceIndex, UtaAudioRouter audioRouter)
    {
        this.deviceIndex = deviceIndex;
        this.audioRouter = audioRouter;
    }

    public override bool Initialize(GameHost host)
    {
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

            if (monitorStream != 0 && monitorVolume > 0)
                Bass.StreamPutData(monitorStream, interleaved, length);

            for (int frame = 0; frame < interleavedCount / channels; frame++)
            {
                float mono = 0;
                for (int channel = 0; channel < channels; channel++)
                    mono += interleaved[frame * channels + channel];

                if (sampleCount < samples.Length)
                    samples[sampleCount++] = mono / channels;

                if (sampleCount == samples.Length)
                {
                    PitchDetected?.Invoke(UtaPitchDetector.Detect(samples, frequency));
                    Array.Copy(samples, samples.Length / 2, samples, 0, samples.Length / 2);
                    sampleCount = samples.Length / 2;
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(interleaved);
        }

        return true;
    }

    private void stop()
    {
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
        sampleCount = 0;
        PitchDetected?.Invoke(null);
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
        base.Dispose(isDisposing);
    }
}
