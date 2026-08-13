// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using ManagedBass;

namespace osu.Game.Rulesets.Karaoke.Integration.Uta;

internal static class UtaMicrophoneDevices
{
    public static IReadOnlyList<UtaMicrophoneDevice> Enumerate()
    {
        var devices = new List<UtaMicrophoneDevice>();

        for (int i = 0; i < Bass.RecordingDeviceCount; i++)
        {
            DeviceInfo info = Bass.RecordGetDeviceInfo(i);
            // BASS reports recording device types as Unknown on Linux.
            if (!info.IsEnabled || (!OperatingSystem.IsLinux() && info.Type != DeviceType.Microphone))
                continue;

            devices.Add(new UtaMicrophoneDevice(i, info.Name));
        }

        return devices;
    }

    public static int Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Bass.DefaultDevice;

        foreach (UtaMicrophoneDevice device in Enumerate())
        {
            if (string.Equals(device.Name, name, StringComparison.Ordinal))
                return device.Index;
        }

        return Bass.DefaultDevice;
    }
}

internal readonly record struct UtaMicrophoneDevice(int Index, string Name);
