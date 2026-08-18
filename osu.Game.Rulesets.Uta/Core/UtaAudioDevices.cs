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

    public static int Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var match = Enumerate().FirstOrDefault(device => device.Name == name);
            if (match.Index > 0)
                return match.Index;
        }

        int current = Bass.CurrentDevice;
        if (current > 0)
            return current;

        return Enumerate().FirstOrDefault(device => Bass.GetDeviceInfo(device.Index).IsDefault).Index is > 0 and var defaultDevice
            ? defaultDevice
            : 1;
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
