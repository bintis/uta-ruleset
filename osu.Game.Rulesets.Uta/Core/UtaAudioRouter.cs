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
/// Owns one BASS mix bus per physical output. BGM, vocals and microphone
/// monitoring routed to the same device therefore share one software mixer.
/// </summary>
internal sealed class UtaAudioRouter : IDisposable
{
    private readonly Dictionary<int, int> buses = new();
    private static readonly object plugin_lock = new();
    private static int flacPlugin;
    private int defaultDevice;

    public void Initialise(AudioManager manager)
    {
        if (defaultDevice != 0)
            return;

        defaultDevice = UtaAudioDevices.Resolve(manager.AudioDevice.Value);
        LoadBundledFlacPlugin();
    }

    public UtaRoutedAudioStream CreateTrack(string filePath, string? outputDevice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        int device = resolve(outputDevice);
        int previous = Bass.CurrentDevice;
        ensureInitialised(device);
        Bass.CurrentDevice = device;

        int source = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        Errors error = Bass.LastError;
        if (previous > 0)
            Bass.CurrentDevice = previous;
        if (source == 0)
            throw new InvalidOperationException($"Could not decode routed audio file: {error}");

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

    public UtaRoutedAudioStream CreateTrack(byte[] data, string? outputDevice)
    {
        int device = resolve(outputDevice);
        int previous = Bass.CurrentDevice;
        ensureInitialised(device);
        Bass.CurrentDevice = device;

        int source = Bass.CreateStream(data, 0, data.Length, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
        Errors error = Bass.LastError;

        if (previous > 0)
            Bass.CurrentDevice = previous;

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

    public int CreateMonitor(int frequency, int channels, string? outputDevice)
    {
        int device = resolve(outputDevice);
        int previous = Bass.CurrentDevice;
        ensureInitialised(device);
        Bass.CurrentDevice = device;
        int stream = Bass.CreateStream(frequency, channels, BassFlags.Decode | BassFlags.Float, StreamProcedureType.Push);
        if (previous > 0)
            Bass.CurrentDevice = previous;

        if (stream != 0)
            route(stream, device, false);
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
            if (previous > 0)
                Bass.CurrentDevice = previous;
        }
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

            BassFlags flags = paused ? BassFlags.MixerChanPause : BassFlags.Default;
            if (!BassMix.MixerAddChannel(getBus(device), source, flags))
                throw new InvalidOperationException($"Could not add Uta source to output mixer: {Bass.LastError}");
        }
        finally
        {
            if (previous > 0)
                Bass.CurrentDevice = previous;
        }
    }

    private int getBus(int device)
    {
        if (buses.TryGetValue(device, out int bus))
            return bus;

        int previous = Bass.CurrentDevice;
        ensureInitialised(device);
        Bass.CurrentDevice = device;
        bus = BassMix.CreateMixerStream(48000, 2, BassFlags.Float | BassFlags.MixerNonStop);
        if (bus != 0)
            Bass.ChannelPlay(bus);
        if (previous > 0)
            Bass.CurrentDevice = previous;

        if (bus == 0)
            throw new InvalidOperationException($"Could not create output mixer for '{Bass.GetDeviceInfo(device).Name}': {Bass.LastError}");

        buses.Add(device, bus);
        Logger.Log($"Uta output mixer ready: {Bass.GetDeviceInfo(device).Name}");
        return bus;
    }

    private int resolve(string? name) => string.IsNullOrWhiteSpace(name) ? defaultDevice : UtaAudioDevices.Resolve(name);

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
        foreach (int bus in buses.Values)
            Bass.StreamFree(bus);
        buses.Clear();
    }
}

internal sealed class UtaRoutedAudioStream : IDisposable
{
    private readonly UtaAudioRouter router;
    private int device;
    private float volume = 1;
    private float appliedTempo = float.NaN;
    private float appliedPitch = float.NaN;
    public int Handle { get; }
    public bool IsRunning { get; private set; }

    internal UtaRoutedAudioStream(UtaAudioRouter router, int handle, int device)
    {
        this.router = router;
        Handle = handle;
        this.device = device;
    }

    public void SetOutput(string? outputDevice)
    {
        device = router.Route(Handle, outputDevice, !IsRunning);
        SetVolume(volume);
    }

    public void SetVolume(float volume)
    {
        this.volume = volume;
        if (!onDevice(() => Bass.ChannelSetAttribute(Handle, ChannelAttribute.Volume, volume)))
            Logger.Log($"Could not set Uta routed-track volume: {Bass.LastError}", level: LogLevel.Error);
    }

    public void SetRate(double rate)
    {
        float tempo = ((float)Math.Abs(rate) - 1) * 100;
        if (tempo.Equals(appliedTempo))
            return;

        if (onDevice(() => Bass.ChannelSetAttribute(Handle, ChannelAttribute.Tempo, tempo)))
            appliedTempo = tempo;
    }

    public void SetPitch(int semitones)
    {
        if ((float)semitones == appliedPitch)
            return;

        if (onDevice(() => Bass.ChannelSetAttribute(Handle, ChannelAttribute.Pitch, semitones)))
            appliedPitch = semitones;
    }

    public void Seek(double time)
        => onDevice(() => Bass.ChannelSetPosition(Handle, Bass.ChannelSeconds2Bytes(Handle, Math.Max(0, time) / 1000)));

    /// <summary>The stream's actual playback position, for comparing against the expected gameplay-clock-derived target.</summary>
    public double GetPositionMs()
        => onDevice(() => Bass.ChannelBytes2Seconds(Handle, Bass.ChannelGetPosition(Handle)) * 1000);

    public void Start()
    {
        onDevice(() => BassMix.ChannelRemoveFlag(Handle, BassFlags.MixerChanPause));
        IsRunning = true;
    }

    public void Stop()
    {
        onDevice(() => BassMix.ChannelAddFlag(Handle, BassFlags.MixerChanPause));
        IsRunning = false;
    }

    public void Dispose()
    {
        onDevice(() =>
        {
            BassMix.MixerRemoveChannel(Handle);
            return Bass.StreamFree(Handle);
        });
    }

    private T onDevice<T>(Func<T> action)
    {
        int previous = Bass.CurrentDevice;
        if (device > 0)
            Bass.CurrentDevice = device;

        try
        {
            return action();
        }
        finally
        {
            if (previous > 0)
                Bass.CurrentDevice = previous;
        }
    }
}
