// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Reflection;
using ManagedBass;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.Karaoke.Integration.Uta;

/// <summary>
/// Registers optional BASS format plugins shipped beside the ruleset. Some Nix
/// lazer packages include core BASS but omit BASSFLAC, causing valid FLAC files
/// to be exposed as zero-length tracks.
/// </summary>
internal static class UtaBassPluginLoader
{
    private static readonly object loadLock = new();
    private static bool attempted;

    public static void EnsureLoaded()
    {
        lock (loadLock)
        {
            if (attempted)
                return;

            attempted = true;
            string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;

            foreach (string filename in new[] { "libbassflac.so", "bassflac.dll", "libbassflac.dylib" })
            {
                string path = Path.Combine(assemblyDirectory, filename);
                if (!File.Exists(path))
                    continue;

                try
                {
                    int handle = Bass.PluginLoad(path);
                    if (handle != 0)
                    {
                        Logger.Log($"Loaded BASSFLAC for UTZ audio from '{path}' (handle {handle}).");
                        return;
                    }

                    Logger.Log($"BASS rejected FLAC plugin '{path}': {Bass.LastError}.", level: LogLevel.Error);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Could not register FLAC plugin '{path}'.");
                }
            }

            Logger.Log("No BASSFLAC library was found beside the karaoke ruleset.", level: LogLevel.Error);
        }
    }
}
