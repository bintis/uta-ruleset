// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using ManagedBass;
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

    private const int window_size = 2048;
    private readonly float[] samples = new float[window_size];
    private int stream;
    private int frequency;
    private int channels;
    private int sampleCount;

    public override bool Initialize(GameHost host)
    {
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
        if (!Bass.RecordInit(Bass.DefaultDevice))
        {
            Logger.Log($"Uta microphone unavailable: {Bass.LastError}", level: LogLevel.Error);
            return;
        }

        RecordInfo info = Bass.RecordingInfo;
        frequency = info.Frequency > 0 ? info.Frequency : 48000;
        channels = Math.Max(1, info.Channels);
        stream = Bass.RecordStart(frequency, channels, BassFlags.Float, 10 * channels, receive);

        if (stream == 0)
        {
            Logger.Log($"Uta microphone could not start: {Bass.LastError}", level: LogLevel.Error);
            stop();
            return;
        }

        Bass.ChannelPlay(stream);
    }

    private bool receive(int handle, IntPtr buffer, int length, IntPtr user)
    {
        int interleavedCount = length / sizeof(float);
        float[] interleaved = ArrayPool<float>.Shared.Rent(interleavedCount);
        try
        {
            Marshal.Copy(buffer, interleaved, 0, interleavedCount);

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
        if (stream != 0)
            Bass.ChannelPause(stream);
        Bass.RecordFree();
        stream = 0;
        sampleCount = 0;
        PitchDetected?.Invoke(null);
    }

    protected override void Dispose(bool isDisposing)
    {
        stop();
        base.Dispose(isDisposing);
    }
}
