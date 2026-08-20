// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using ManagedBass;

namespace osu.Game.Rulesets.Uta.Core;

internal static class UtaAudioDevices
{
    public static IEnumerable<(int Index, string Name)> Enumerate()
    {
        for (int i = 1; i < Bass.DeviceCount; i++)
        {
            DeviceInfo info = Bass.GetDeviceInfo(i);
            if (info.IsEnabled && !string.IsNullOrWhiteSpace(info.Name))
                yield return (i, info.Name);
        }
    }

    public static bool IsPlaceholderOutput(string? name)
        => string.IsNullOrWhiteSpace(name)
           || name.Equals("Default", StringComparison.OrdinalIgnoreCase)
           || name.Equals("No Sound", StringComparison.OrdinalIgnoreCase)
           || name.Equals("Default Audio Device", StringComparison.OrdinalIgnoreCase);

    public static bool IsInitialisedOutput(int device)
    {
        if (device < 1)
            return false;

        DeviceInfo info = Bass.GetDeviceInfo(device);
        return info.IsInitialized;
    }

    /// <summary>
    /// Prefer an already-initialised BASS output. A device named Default that
    /// osu already opened is valid; an uninitialised named-device index is not
    /// (second-device Init on Pulse is Parameter / Init).
    /// </summary>
    public static int SkipPlaceholder(int device)
    {
        if (IsInitialisedOutput(device))
            return device;

        return Resolve(null);
    }

    public static int Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !IsPlaceholderOutput(name))
        {
            var match = Enumerate().FirstOrDefault(device => device.Name == name);
            if (match.Index > 0 && IsInitialisedOutput(match.Index))
                return match.Index;
        }

        int current = Bass.CurrentDevice;
        if (IsInitialisedOutput(current))
            return current;

        var markedDefault = Enumerate().FirstOrDefault(device =>
            !IsPlaceholderOutput(device.Name) && IsInitialisedOutput(device.Index));
        if (markedDefault.Index > 0)
            return markedDefault.Index;

        var firstReal = Enumerate().FirstOrDefault(device =>
            !IsPlaceholderOutput(device.Name) && IsInitialisedOutput(device.Index));
        return firstReal.Index > 0 ? firstReal.Index : current;
    }
}

internal static class UtaDeviceItems
{
    public static string[] Build(string? preferred, IEnumerable<string> available)
    {
        var items = new List<string> { string.Empty };
        items.AddRange(available.Where(name => !string.IsNullOrWhiteSpace(name)));

        if (!string.IsNullOrEmpty(preferred) && !items.Contains(preferred, StringComparer.Ordinal))
            items.Add(preferred);

        return items.Distinct(StringComparer.Ordinal).ToArray();
    }
}
