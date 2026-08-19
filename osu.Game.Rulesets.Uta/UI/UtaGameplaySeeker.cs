// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Reflection;
using osu.Framework.Logging;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Seeks lazer's gameplay clock (TrackBass). Frame-stable playback is disabled
/// for Uta so a BASS position jump cannot freeze the clock.
/// </summary>
internal static class UtaGameplaySeeker
{
    private static readonly PropertyInfo? frame_stable_playback = typeof(DrawableRuleset).GetProperty(
        "FrameStablePlayback", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void DisableFrameStability(DrawableRuleset drawableRuleset)
        => frame_stable_playback?.SetValue(drawableRuleset, false);

    public static bool Seek(GameplayClockContainer gameplayClock, double target, string logContext, bool debugLog)
    {
        double current = gameplayClock.CurrentTime;
        if (gameplayClock.IsPaused.Value || !gameplayClock.IsRunning)
            return false;

        try
        {
            gameplayClock.Seek(Math.Max(0, target));
            if (debugLog)
                Logger.Log($"Uta {logContext}: {current:N0} ms -> {target:N0} ms.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Uta {logContext} failed: {ex.GetBaseException().Message}", level: LogLevel.Error);
            return false;
        }
    }
}
