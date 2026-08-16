// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Reflection;
using osu.Framework.Logging;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Performs a large gameplay-clock seek (gap skip, A-B loop repeat, phrase navigation) safely.
/// A jump beyond lazer's frame-stable-playback threshold is otherwise treated as an invalid BASS
/// jump; frame stability is switched off only for the duration of this seek, matching the
/// workaround <see cref="UtaGapSkipController"/> already used for gap skipping.
/// </summary>
internal static class UtaGameplaySeeker
{
    private static readonly PropertyInfo? frame_stable_playback = typeof(DrawableRuleset).GetProperty(
        "FrameStablePlayback", BindingFlags.Instance | BindingFlags.NonPublic);

    // schedule is the caller's own Drawable.Schedule(Action) - passed in because it's protected
    // and this helper isn't itself a Drawable.
    public static bool Seek(GameplayClockContainer gameplayClock, DrawableRuleset drawableRuleset, Action<Action> schedule,
                            double target, string logContext, bool debugLog)
    {
        double current = gameplayClock.CurrentTime;
        if (gameplayClock.IsPaused.Value || !gameplayClock.IsRunning)
            return false;

        target = Math.Max(0, target);
        bool restoreFrameStability = frame_stable_playback?.GetValue(drawableRuleset) is true;

        try
        {
            if (restoreFrameStability)
                frame_stable_playback!.SetValue(drawableRuleset, false);

            gameplayClock.Seek(target);
            if (restoreFrameStability)
                schedule(() => frame_stable_playback!.SetValue(drawableRuleset, true));

            if (debugLog)
                Logger.Log($"Uta {logContext}: {current:N0} ms -> {target:N0} ms.");

            return true;
        }
        catch (Exception ex)
        {
            if (restoreFrameStability)
                frame_stable_playback!.SetValue(drawableRuleset, true);

            Logger.Log($"Uta {logContext} failed: {ex.GetBaseException().Message}", level: LogLevel.Error);
            return false;
        }
    }
}
