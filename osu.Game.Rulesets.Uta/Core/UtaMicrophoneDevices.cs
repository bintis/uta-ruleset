// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using ManagedBass;

namespace osu.Game.Rulesets.Uta.Core;

internal static class UtaMicrophoneDevices
{
    public static IEnumerable<(int Index, string Name)> Enumerate()
    {
        for (int i = 0; i < Bass.RecordingDeviceCount; i++)
        {
            if (!Bass.RecordGetDeviceInfo(i, out DeviceInfo info))
                continue;
            if (info.IsEnabled && !string.IsNullOrWhiteSpace(info.Name))
                yield return (i, info.Name);
        }
    }

    public static int Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var match = Enumerate().FirstOrDefault(device => device.Name == name);
            if (match.Name != null)
                return match.Index;
        }

        return 0;
    }
}
