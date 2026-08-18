// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.IO;
using osu.Game.Rulesets.Uta.Recording;

namespace osu.Game.Rulesets.Uta.Storage;

public static class UtaStoragePaths
{
    public static string QueueFile => Path.Combine(UtaPerformanceRootRegistry.Resolve(), "queue.json");
    public static string RemoteDevicesFile => Path.Combine(UtaPerformanceRootRegistry.Resolve(), "remote-devices.json");
}
