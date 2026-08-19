// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using ManagedBass;
using ManagedBass.Fx;
using ManagedBass.Mix;
using osu.Framework.Audio;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.Uta.Core;

/// <summary>
/// One process-wide BASS mix bus per physical output. Used only when a route
/// must leave lazer's default TrackBass mixer (another device, or a push monitor).
/// </summary>
internal sealed class UtaAudioRouter : IDisposable
{
    private static readonly object plugin_lock = new();
    private static readonly object bus_lock = new();
    private static readonly Dictionary<int, int> buses = new();
    private static readonly HashSet<int> protected_sources = new();
    private static int flacPlugin;
    private int defaultDevice;

    public int DefaultDevice => defaultDevice;

    public void Initialise(AudioManager manager)
    {
        if (defaultDevice != 0)
            return;

        defaultDevice = UtaAudioDevices.Resolve(manager.AudioDevice.Value);
        Logger.Log($"Uta audio output resolved: '{safeDeviceName(defaultDevice)}' requested='{manager.AudioDevice.Value}'");
        LoadBundledFlacPlugin();
    }

    public static void HaltAllPlayback()
    {
        try
        {
            int streams = UtaRoutedAudioStream.HaltAll();
            int mixerChannels = drainBuses();
            int stopped = stopBuses();
            Logger.Log($"Uta halted leftover playback: streams={streams} mixerChannels={mixerChannels} busesStopped={stopped}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Uta leftover halt failed: {ex.Message}", level: LogLevel.Error);
        }
    }

    public static void DestroyBuses()
    {
        HaltAllPlayback();
        lock (bus_lock)
        {
            foreach ((int device, int bus) in buses)
            {
                int previous = Bass.CurrentDevice;
                if (device > 0)
                    Bass.CurrentDevice = device;
                try
                {
                    Bass.ChannelStop(bus);
                    Bass.StreamFree(bus);
                }
                finally
                {
                    Bass.CurrentDevice = previous;
                }
            }

            buses.Clear();
            protected_sources.Clear();
        }
    }

    private static int stopBuses()
    {
        int stopped = 0;
        lock (bus_lock)
        {
            foreach ((int device, int bus) in buses)
            {
                int previous = Bass.CurrentDevice;
                if (device > 0)
                    Bass.CurrentDevice = device;
                try
                {
                    if (Bass.ChannelStop(bus))
                        stopped++;
                }
                finally
                {
                    Bass.CurrentDevice = previous;
                }
            }
        }

        return stopped;
    }

    public void ProtectSource(int handle)
    {
        if (handle == 0)
            return;

        lock (bus_lock)
            protected_sources.Add(handle);
    }

    public void UnprotectSource(int handle)
    {
        if (handle == 0)
            return;

        lock (bus_lock)
            protected_sources.Remove(handle);
    }

    private static int drainBuses()
    {
        int drained = 0;

        lock (bus_lock)
        {
            foreach ((int device, int bus) in buses)
            {
                int previous = Bass.CurrentDevice;
                if (device > 0)
                    Bass.CurrentDevice = device;

                try
                {
                    int[]? channels = BassMix.MixerGetChannels(bus);
                    if (channels == null)
                        continue;

                    foreach (int channel in channels)
                    {
                        if (protected_sources.Contains(channel))
                            continue;

                        BassMix.MixerRemoveChannel(channel);
                        Bass.ChannelStop(channel);
                        Bass.StreamFree(channel);
                        drained++;
                    }
                }
                finally
                {
                    Bass.CurrentDevice = previous;
                }
            }
        }

        return drained;
    }

    public UtaRoutedAudioStream CreateTrack(string filePath, string? outputDevice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return createTempoStream(
            device => Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan),
            outputDevice);
    }

    public UtaRoutedAudioStream CreateTrack(byte[] data, string? outputDevice)
        => createTempoStream(
            device => Bass.CreateStream(data, 0, data.Length, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan),
            outputDevice);

    public int CreateMonitor(int frequency, int channels, string? outputDevice)
    {
        int device = resolve(outputDevice);
        int previous = Bass.CurrentDevice;
        int stream;
        try
        {
            ensureInitialised(device);
            Bass.CurrentDevice = device;
            stream = Bass.CreateStream(frequency, channels, BassFlags.Decode | BassFlags.Float, StreamProcedureType.Push);
        }
        finally
        {
            Bass.CurrentDevice = previous;
        }

        if (stream != 0)
        {
            route(stream, device, false);
            ProtectSource(stream);
        }

        return stream;
    }

    public int Route(int source, string? outputDevice, bool paused)
    {
        int device = resolve(outputDevice);
        route(source, device, paused);
        return device;
    }

    public int GetOutputLatency(string? outputDevice)
    {
        int device = resolve(outputDevice);
        int previous = Bass.CurrentDevice;
        try
        {
            ensureInitialised(device);
            Bass.CurrentDevice = device;
            return Bass.Info.Latency;
        }
        finally
        {
            // Always restore, including Bass.DefaultDevice (-1). Skipping that
            // leaves CurrentDevice on the Uta mixer and the next native VOX
            // TrackBass is created on the wrong graph (AUDIO leftover doc §24).
            Bass.CurrentDevice = previous;
        }
    }

    private UtaRoutedAudioStream createTempoStream(Func<int, int> createSource, string? outputDevice)
    {
        int device = resolve(outputDevice);
        int previous = Bass.CurrentDevice;
        int source;
        Errors error;
        try
        {
            ensureInitialised(device);
            Bass.CurrentDevice = device;
            source = createSource(device);
            error = Bass.LastError;
        }
        finally
        {
            // Track creation can throw (missing/corrupt audio). Restore even then,
            // otherwise the next native TrackBass/VOX lands on Uta's mixer device.
            Bass.CurrentDevice = previous;
        }

        if (source == 0)
            throw new InvalidOperationException($"Could not decode routed audio: {error}");

        int tempo = BassFx.TempoCreate(source, BassFlags.Decode | BassFlags.FxFreeSource);
        if (tempo == 0)
        {
            error = Bass.LastError;
            Bass.StreamFree(source);
            throw new InvalidOperationException($"Could not create pitch/tempo stream: {error}");
        }

        route(tempo, device, true);
        return new UtaRoutedAudioStream(this, tempo, device);
    }

    private void route(int source, int device, bool paused)
    {
        int previous = Bass.CurrentDevice;

        try
        {
            int sourceDevice = Bass.ChannelGetDevice(source);
            if (sourceDevice > 0)
                Bass.CurrentDevice = sourceDevice;
            BassMix.MixerRemoveChannel(source);
            ensureInitialised(device);
            Bass.CurrentDevice = device;
            if (sourceDevice != device && !Bass.ChannelSetDevice(source, device))
                throw new InvalidOperationException($"Could not move Uta source to '{Bass.GetDeviceInfo(device).Name}': {Bass.LastError}");

            int bus = getBus(device);
            BassFlags flags = paused ? BassFlags.MixerChanPause : BassFlags.Default;
            if (!BassMix.MixerAddChannel(bus, source, flags))
                throw new InvalidOperationException($"Could not add Uta source to output mixer: {Bass.LastError}");
        }
        finally
        {
            // Always restore, including Bass.DefaultDevice (-1). Skipping that
            // leaves CurrentDevice on the Uta mixer and the next native VOX
            // TrackBass is created on the wrong graph (AUDIO leftover doc §24).
            Bass.CurrentDevice = previous;
        }
    }

    private static int getBus(int device)
    {
        lock (bus_lock)
        {
            int requested = device;
            device = UtaAudioDevices.SkipPlaceholder(device);
            if (device != requested)
                Logger.Log($"Uta skipped unusable output '{safeDeviceName(requested)}', using '{safeDeviceName(device)}'");

            if (buses.TryGetValue(device, out int cached))
            {
                playBus(device, cached);
                return cached;
            }

            int bus = tryCreateBus(device);
            if (bus == 0)
            {
                int current = Bass.CurrentDevice;
                if (current != device)
                    bus = tryCreateBus(current);
                if (bus != 0)
                {
                    buses.Add(current, bus);
                    Logger.Log($"Uta output mixer ready: {safeDeviceName(current)} (fell back from '{safeDeviceName(device)}')");
                    return bus;
                }

                throw new InvalidOperationException(
                    $"Could not create output mixer for '{safeDeviceName(device)}': {Bass.LastError}");
            }

            buses.Add(device, bus);
            Logger.Log($"Uta output mixer ready: {safeDeviceName(device)}");
            return bus;
        }
    }

    private static int tryCreateBus(int device)
    {
        int previous = Bass.CurrentDevice;
        try
        {
            ensureInitialised(device);
            Bass.CurrentDevice = device;
            // osu's BassAudioMixer is 44100 + MixerNonStop (no Float). 48000+Float
            // is ILLPARAM on the TestScene/default Pulse device (AUDIO leftover doc §26).
            int bus = BassMix.CreateMixerStream(44100, 2, BassFlags.MixerNonStop);
            if (bus != 0)
            {
                Bass.ChannelPlay(bus);
                return bus;
            }

            Logger.Log($"Uta output mixer create failed for '{safeDeviceName(device)}': {Bass.LastError}");
            return 0;
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta output mixer init failed for '{safeDeviceName(device)}': {exception.Message}");
            return 0;
        }
        finally
        {
            Bass.CurrentDevice = previous;
        }
    }

    private static void playBus(int device, int bus)
    {
        int previousDevice = Bass.CurrentDevice;
        try
        {
            if (device > 0)
                Bass.CurrentDevice = device;
            Bass.ChannelPlay(bus);
        }
        finally
        {
            Bass.CurrentDevice = previousDevice;
        }
    }

    private static string safeDeviceName(int device)
    {
        DeviceInfo info = Bass.GetDeviceInfo(device);
        return string.IsNullOrWhiteSpace(info.Name) ? $"device {device}" : info.Name;
    }

    internal int CaptureDevice() => Bass.CurrentDevice;

    internal void RestoreDevice(int previous) => Bass.CurrentDevice = previous;

    internal void UseDefaultDevice()
    {
        if (defaultDevice > 0)
            Bass.CurrentDevice = defaultDevice;
    }

    private int resolve(string? name)
    {
        int device = string.IsNullOrWhiteSpace(name) ? defaultDevice : UtaAudioDevices.Resolve(name);
        return UtaAudioDevices.SkipPlaceholder(device);
    }

    private static void ensureInitialised(int device)
    {
        DeviceInfo info = Bass.GetDeviceInfo(device);
        if (!info.IsInitialized && !Bass.Init(device))
            throw new InvalidOperationException($"Could not initialise audio output '{info.Name}': {Bass.LastError}");
    }

    internal static void LoadBundledFlacPlugin()
    {
        if (!OperatingSystem.IsLinux() || flacPlugin != 0)
            return;

        lock (plugin_lock)
        {
            if (flacPlugin != 0)
                return;

            string path = Path.Combine(Path.GetDirectoryName(typeof(UtaAudioRouter).Assembly.Location)!, "libbassflac.so");
            if (!File.Exists(path))
            {
                Logger.Log($"Bundled BASSFLAC plugin was not found at '{path}'.");
                return;
            }

            flacPlugin = Bass.PluginLoad(path);
            if (flacPlugin == 0)
                Logger.Log($"Could not load bundled BASSFLAC plugin: {Bass.LastError}", level: LogLevel.Error);
            else
                Logger.Log("Bundled BASSFLAC plugin loaded.");
        }
    }

    public void Dispose()
    {
        // Buses are process-wide so a late DrawableRuleset dispose cannot free
        // the mixer the next chart is already playing through.
    }
}

internal sealed class UtaRoutedAudioStream : IDisposable
{
    private static readonly object live_lock = new();
    private static readonly List<UtaRoutedAudioStream> live = new();

    private readonly UtaAudioRouter router;
    private int handle;
    private int device;
    private readonly float baseFrequency;
    private float volume = 1;
    private float appliedTempo = float.NaN;
    private float appliedFrequency = float.NaN;
    public int Handle => handle;
    public bool IsRunning { get; private set; }

    internal UtaRoutedAudioStream(UtaAudioRouter router, int handle, int device)
    {
        this.router = router;
        this.handle = handle;
        this.device = device;
        float frequency = 44100;
        onDevice(() => Bass.ChannelGetAttribute(handle, ChannelAttribute.Frequency, out frequency));
        baseFrequency = frequency > 0 ? frequency : 44100;
        lock (live_lock)
            live.Add(this);
    }

    internal static int HaltAll()
    {
        UtaRoutedAudioStream[] snapshot;
        lock (live_lock)
        {
            snapshot = live.ToArray();
            live.Clear();
        }

        int halted = 0;
        foreach (UtaRoutedAudioStream stream in snapshot)
        {
            if (stream.release())
                halted++;
        }

        return halted;
    }

    public void SetOutput(string? outputDevice)
    {
        if (handle == 0)
            return;

        device = router.Route(handle, outputDevice, !IsRunning);
        SetVolume(volume);
    }

    public void SetVolume(float volume)
    {
        this.volume = volume;
        if (handle == 0)
            return;

        if (!onDevice(() => Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, volume)))
            Logger.Log($"Could not set Uta routed-track volume: {Bass.LastError}", level: LogLevel.Error);
    }

    public void SetFrequency(double relative)
    {
        if (handle == 0)
            return;

        float value = (float)Math.Abs(relative);
        if (value.Equals(appliedFrequency))
            return;

        if (onDevice(() => Bass.ChannelSetAttribute(handle, ChannelAttribute.Frequency, Math.Max(100, baseFrequency * value))))
            appliedFrequency = value;
    }

    public void SetTempo(double relative)
    {
        if (handle == 0)
            return;

        float tempo = ((float)Math.Abs(relative) - 1) * 100;
        if (tempo.Equals(appliedTempo))
            return;

        if (onDevice(() => Bass.ChannelSetAttribute(handle, ChannelAttribute.Tempo, tempo)))
            appliedTempo = tempo;
    }

    public void Seek(double time)
    {
        if (handle == 0)
            return;

        onDevice(() => Bass.ChannelSetPosition(handle, Bass.ChannelSeconds2Bytes(handle, Math.Max(0, time) / 1000)));
    }

    public double GetPositionMs()
    {
        if (handle == 0)
            return 0;

        return onDevice(() => Bass.ChannelBytes2Seconds(handle, Bass.ChannelGetPosition(handle)) * 1000);
    }

    public void Start()
    {
        if (handle == 0)
            return;

        onDevice(() => BassMix.ChannelRemoveFlag(handle, BassFlags.MixerChanPause));
        IsRunning = true;
    }

    public void Stop()
    {
        if (handle == 0)
        {
            IsRunning = false;
            return;
        }

        onDevice(() => BassMix.ChannelAddFlag(handle, BassFlags.MixerChanPause));
        IsRunning = false;
    }

    public void Dispose()
    {
        lock (live_lock)
            live.Remove(this);
        release();
    }

    private bool release()
    {
        int current = handle;
        if (current == 0)
            return false;

        handle = 0;
        IsRunning = false;
        int previous = Bass.CurrentDevice;
        if (device > 0)
            Bass.CurrentDevice = device;

        try
        {
            BassMix.MixerRemoveChannel(current);
            Bass.ChannelStop(current);
            Bass.StreamFree(current);
            return true;
        }
        finally
        {
            // Always restore, including Bass.DefaultDevice (-1). Skipping that
            // leaves CurrentDevice on the Uta mixer and the next native VOX
            // TrackBass is created on the wrong graph (AUDIO leftover doc §24).
            Bass.CurrentDevice = previous;
        }
    }

    private T onDevice<T>(Func<T> action)
    {
        if (handle == 0)
            return default!;

        int previous = Bass.CurrentDevice;
        if (device > 0)
            Bass.CurrentDevice = device;

        try
        {
            return action();
        }
        finally
        {
            // Always restore, including Bass.DefaultDevice (-1). Skipping that
            // leaves CurrentDevice on the Uta mixer and the next native VOX
            // TrackBass is created on the wrong graph (AUDIO leftover doc §24).
            Bass.CurrentDevice = previous;
        }
    }
}
