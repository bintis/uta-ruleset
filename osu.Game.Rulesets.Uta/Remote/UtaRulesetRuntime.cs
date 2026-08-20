// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Gameplay;
using osu.Game.Rulesets.Uta.Library;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Playback;
using osu.Game.Rulesets.Uta.Queue;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.Uta.Remote;

/// <summary>
/// Process-lifetime, non-drawable state retained by the ruleset after gameplay exits.
/// It never accesses osu! screens or input without an attached Uta gameplay lease.
/// </summary>
internal sealed class UtaRulesetRuntime : IDisposable
{
    private static readonly Lazy<UtaRulesetRuntime> instance = new(() => new UtaRulesetRuntime());

    public static UtaRulesetRuntime Instance => instance.Value;

    public UtaSongQueueService Queue { get; } = new();
    public UtaGameplaySessionRegistry Sessions { get; } = new();
    public BindableBool AutoAdvanceEnabled { get; } = new(true);
    public Bindable<UtaPlaybackTransitionState> TransitionState { get; } = new(UtaPlaybackTransitionState.Idle);
    public DateTimeOffset TransitionStartedAt { get; set; }
    public QueueReservation? PendingReservation { get; set; }
    public Guid PendingBeatmapId { get; set; }
    public double? PendingSpeed { get; set; }
    public Guid PendingSpeedBeatmapId { get; set; }

    /// <summary>
    /// The beatmap bindable visible to the current Uta drawable tree. After the
    /// first Player.Restart this is usually a returned lease, not the game-wide
    /// bindable SongSelect still holds.
    /// </summary>
    public Bindable<WorkingBeatmap>? GameBeatmap { get; private set; }
    public BeatmapManager? Beatmaps { get; set; }

    public WorkingBeatmap? LastPlayedBeatmap { get; private set; }

    /// <summary>
    /// Last explicit original-vocals preference. Empty constructor mods after
    /// 切歌 are not an off switch (AUDIO leftover doc §24).
    /// </summary>
    public bool OriginalVocalsPreferred { get; private set; }
    public int? SelectedTranspose { get; private set; }
    public double? SelectedCoreRate { get; private set; }
    public bool AuthoritativeModSelectionKnown => authoritativeSelectionKnown;

    private bool originalVocalsPreferenceSeeded;

    private Bindable<WorkingBeatmap>? songSelectBeatmap;
    private Bindable<WorkingBeatmap>? gameWideBeatmap;
    private bool watchingSongSelectBeatmap;
    private bool screenExitHooked;
    private ScreenStack? screenStack;
    private MusicController? musicController;
    private Bindable<IReadOnlyList<Mod>>? selectedMods;
    private bool songSelectSelectionActive;
    private bool authoritativeSelectionKnown;

    private static readonly FieldInfo? user_pause_requested_field = typeof(MusicController).GetField(
        "<UserPauseRequested>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? user_pause_requested = typeof(MusicController).GetProperty(
        nameof(MusicController.UserPauseRequested));

    private static readonly MethodInfo? change_music_beatmap = typeof(MusicController).GetMethod(
        "changeBeatmap",
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        types: new[] { typeof(WorkingBeatmap) },
        modifiers: null);

    public UtaRemoteCommandRouter CommandRouter { get; }
    public UtaRemoteServerController RemoteServerController { get; }

    private bool disposed;

    private UtaRulesetRuntime()
    {
        CommandRouter = new UtaRemoteCommandRouter(Queue, Sessions, AutoAdvanceEnabled);
        RemoteServerController = new UtaRemoteServerController(CommandRouter, CommandRouter.GetSnapshot);
        Queue.Changed += broadcastQueue;
        AutoAdvanceEnabled.BindValueChanged(_ => broadcastQueue());
        AppDomain.CurrentDomain.ProcessExit += onProcessExit;
    }

    public IDisposable AttachGameplayServices(UtaSongLibrary library, UtaPlaybackCoordinator playback)
        => CommandRouter.AttachGameplayServices(library, playback);

    public void AttachMusicController(MusicController? music)
    {
        if (music == null || ReferenceEquals(musicController, music))
            return;

        musicController = music;
        Logger.Log("Uta attached MusicController.");
    }

    /// <summary>
    /// Observe the process-wide mod selection beyond the lifetime of the first
    /// DrawableRuleset. SongSelect changes are authoritative, while narrowed
    /// PlayerLoader values may only contribute positive state during gameplay.
    /// </summary>
    public void AttachSelectedMods(Bindable<IReadOnlyList<Mod>>? mods)
    {
        if (mods == null || ReferenceEquals(selectedMods, mods))
            return;

        if (selectedMods != null)
            selectedMods.ValueChanged -= onSelectedModsChanged;

        selectedMods = mods;
        selectedMods.ValueChanged += onSelectedModsChanged;
        rememberPositiveModState(selectedMods.Value);
        Logger.Log(
            $"Uta attached game-wide selected mods: [{formatMods(selectedMods.Value)}] "
            + $"rememberedTranspose={SelectedTranspose?.ToString() ?? "none"} coreRate={SelectedCoreRate?.ToString("0.00") ?? "unknown"}.");
    }

    private void onSelectedModsChanged(ValueChangedEvent<IReadOnlyList<Mod>> change)
    {
        bool vox = change.NewValue.Any(mod => mod is UtaModOriginalVocals);
        if (songSelectSelectionActive)
            rememberAuthoritativeModState(change.NewValue);
        else
            rememberPositiveModState(change.NewValue);

        Logger.Log(
            $"Uta game-wide selected mods changed: [{formatMods(change.OldValue)}] -> [{formatMods(change.NewValue)}] "
            + $"authoritative={songSelectSelectionActive} vox={vox} preferred={OriginalVocalsPreferred} "
            + $"rememberedTranspose={SelectedTranspose?.ToString() ?? "none"} coreRate={SelectedCoreRate?.ToString("0.00") ?? "unknown"}.");
    }

    private void rememberPositiveModState(IReadOnlyList<Mod> mods)
    {
        if (authoritativeSelectionKnown)
            return;

        (bool vocals, int? transpose) = ResolveRememberedModState(
            false, OriginalVocalsPreferred, SelectedTranspose, mods);
        RememberOriginalVocals(vocals);
        SelectedTranspose = transpose;
        SelectedCoreRate = ResolveRememberedCoreRate(false, SelectedCoreRate, mods);
    }

    private void rememberAuthoritativeModState(IReadOnlyList<Mod> mods)
    {
        (bool vocals, int? transpose) = ResolveRememberedModState(
            true, OriginalVocalsPreferred, SelectedTranspose, mods);
        RememberOriginalVocals(vocals);
        SelectedTranspose = transpose;
        SelectedCoreRate = ResolveRememberedCoreRate(true, SelectedCoreRate, mods);
        authoritativeSelectionKnown = true;
    }

    internal static double? ResolveRememberedCoreRate(bool authoritative, double? previousRate, IReadOnlyList<Mod> mods)
    {
        double? selectedRate = mods.Any(mod => mod is UtaModNightcore)
            ? 1.5
            : mods.Any(mod => mod is UtaModDaycore)
                ? 0.75
                : null;

        return authoritative ? selectedRate ?? 1 : previousRate ?? selectedRate;
    }

    internal static (bool OriginalVocals, int? Transpose) ResolveRememberedModState(
        bool authoritative,
        bool previousOriginalVocals,
        int? previousTranspose,
        IReadOnlyList<Mod> mods)
    {
        bool hasOriginalVocals = mods.Any(mod => mod is UtaModOriginalVocals);
        UtaModTranspose? transpose = mods.OfType<UtaModTranspose>().FirstOrDefault();

        return authoritative
            ? (hasOriginalVocals, transpose?.Semitones)
            : (previousOriginalVocals || hasOriginalVocals, transpose?.Semitones ?? previousTranspose);
    }

    private static string formatMods(IReadOnlyList<Mod>? mods)
        => mods == null ? string.Empty : string.Join(",", mods.Select(mod => mod.Acronym));

    private sealed class ModIdentityComparer : IEqualityComparer<Mod>
    {
        public static readonly ModIdentityComparer Instance = new();

        public bool Equals(Mod? x, Mod? y)
            => ReferenceEquals(x, y) || (x != null && y != null && x.GetType() == y.GetType() && x.Acronym == y.Acronym);

        public int GetHashCode(Mod obj) => HashCode.Combine(obj.GetType(), obj.Acronym);
    }

    /// <summary>
    /// ScreenStack fires this after PlayerLoader UnbindAll (lease back) and
    /// before SongSelect.OnResuming → beginLooping. Update()-after-leave is
    /// too late and red-screens (AUDIO leftover doc §27).
    /// </summary>
    public void HookScreenExit(ScreenStack? stack)
    {
        if (screenExitHooked || stack == null)
            return;

        screenStack = stack;
        stack.ScreenPushed += onScreenPushed;
        stack.ScreenExited += onScreenExited;
        screenExitHooked = true;
        Logger.Log("Uta hooked ScreenStack push/exit events for SongSelect and leftover leave.");
    }

    private void onScreenPushed(IScreen previous, IScreen next)
    {
        if (previous is SongSelect songSelect && next is not SongSelect)
        {
            rememberAuthoritativeModState(songSelect.Mods.Value);
            reconcilePushedScreenMods(next);
            songSelectSelectionActive = false;
        }

        if (next is SongSelect nextSongSelect)
            attachSongSelect(nextSongSelect, false);
    }

    private void onScreenExited(IScreen exited, IScreen next)
    {
        if (exited is SongSelect)
        {
            songSelectSelectionActive = false;
            AttachSongSelectBeatmap(null, gameWideBeatmap);
        }

        if (next is SongSelect songSelect)
            attachSongSelect(songSelect, true);
    }

    private void attachSongSelect(SongSelect songSelect, bool stopLeftoverPlayback)
    {
        songSelectSelectionActive = true;
        AttachSelectedMods(songSelect.Mods);
        rememberAuthoritativeModState(songSelect.Mods.Value);
        AttachSongSelectBeatmap(songSelect.Beatmap, gameWideBeatmap);

        if (stopLeftoverPlayback)
        {
            UI.UtaAudioController.DestroyAllPlayback();
            StopLeftoverOnLeave();
        }
    }

    private void reconcilePushedScreenMods(IScreen screen)
    {
        if (!authoritativeSelectionKnown)
            return;

        try
        {
            PropertyInfo? property = screen.GetType().GetProperty("Mods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.GetValue(screen) is not Bindable<IReadOnlyList<Mod>> mods)
                return;

            IReadOnlyList<Mod> reconciled = DrawableUtaRuleset.ReconcileConstructorMods(
                mods.Value,
                SelectedCoreRate,
                true,
                SelectedTranspose);

            if (mods.Value.SequenceEqual(reconciled, ModIdentityComparer.Instance))
                return;

            string oldMods = formatMods(mods.Value);
            mods.Value = reconciled;
            Logger.Log(
                $"Uta reconciled pushed screen mods: [{oldMods}] -> [{formatMods(reconciled)}] "
                + $"authoritativeTranspose={SelectedTranspose?.ToString() ?? "none"} coreRate={SelectedCoreRate?.ToString("0.00") ?? "unknown"}.");
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta could not reconcile pushed screen mods: {exception.Message}");
        }
    }

    public void AttachSongSelectBeatmap(Bindable<WorkingBeatmap>? songSelect, Bindable<WorkingBeatmap>? gameWide = null)
    {
        if (!ReferenceEquals(songSelectBeatmap, songSelect))
        {
            if (songSelectBeatmap != null && watchingSongSelectBeatmap)
                songSelectBeatmap.ValueChanged -= onSongSelectBeatmapChanged;

            songSelectBeatmap = songSelect;
            if (songSelectBeatmap != null)
            {
                songSelectBeatmap.ValueChanged += onSongSelectBeatmapChanged;
                watchingSongSelectBeatmap = true;
            }
            else
                watchingSongSelectBeatmap = false;
        }

        if (gameWide != null)
            gameWideBeatmap = gameWide;

        GameBeatmap = songSelect ?? gameWideBeatmap;
        Logger.Log(songSelect == null
            ? "Uta could not attach the song select beatmap bindable."
            : $"Uta attached song select beatmap bindable disabled={songSelect.Disabled} gameWide={(gameWideBeatmap == null ? "missing" : $"disabled={gameWideBeatmap.Disabled}")}.");
    }

    /// <summary>
    /// PlayerLoader's lease can leave both SongSelect and the cached game-wide
    /// copies detached after it returns. Mirror the value, then switch
    /// MusicController to this newly selected chart. Unlike the failed leave path,
    /// this never restarts LastPlayed and never LoadTrack's a private carousel copy
    /// (§§17, 19, 21–23).
    /// </summary>
    private void onSongSelectBeatmapChanged(ValueChangedEvent<WorkingBeatmap> change)
    {
        if (gameWideBeatmap == null || gameWideBeatmap.Disabled
            || ReferenceEquals(gameWideBeatmap.Value, change.NewValue))
            return;

        try
        {
            gameWideBeatmap.Value = change.NewValue;

            // The cached game-wide copy can itself be detached after the gameplay
            // lease. Switch only to the newly selected chart (never LastPlayed on
            // leave), then let MusicController own LoadTrack and DrawableTrack.
            if (musicController != null && change_music_beatmap != null)
            {
                change_music_beatmap.Invoke(musicController, new object[] { change.NewValue });
                musicController.Play(restart: true, requestedByUser: true);
            }

            Logger.Log($"Uta mirrored and started song select preview: {change.NewValue}");
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta could not mirror song select beatmap: {exception.Message}");
        }
    }

    public void RememberPlayedBeatmap(WorkingBeatmap beatmap)
    {
        LastPlayedBeatmap = beatmap;
    }

    public bool TryPublishWorkingBeatmap(WorkingBeatmap target)
    {
        bool published = false;
        published |= trySetBeatmap(songSelectBeatmap, target, "song select");
        published |= trySetBeatmap(gameWideBeatmap, target, "game-wide");
        LastPlayedBeatmap = target;
        return published;
    }

    private bool trySetBeatmap(Bindable<WorkingBeatmap>? bindable, WorkingBeatmap target, string name)
    {
        if (bindable == null)
            return false;

        tryEnableBindable(bindable, name);
        if (bindable.Disabled)
            return false;

        try
        {
            bindable.Value = target;
            Logger.Log($"Uta published {name} beatmap: {target}");
            return true;
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta could not publish {name} beatmap: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stop leftover TrackBass synchronously and mark MusicController paused so
    /// SongSelect.ensurePlayingSelected will not resume the previous chart.
    /// Do not Play the leftover chart afterwards (see AUDIO leftover doc §21).
    /// Do not Return the beatmap lease — ScreenStack UnbindAll already does (§23 / §27).
    /// </summary>
    public void StopLeftoverOnLeave()
    {
        stopTrack(LastPlayedBeatmap, "leave LastPlayed");
        SilenceMusicController();

        // ScreenExited runs after PlayerLoader returns the lease and before
        // SongSelect.OnResuming/beginLooping. Load exactly the instance that
        // beginLooping will read; never manufacture tracks for both bindables.
        EnsurePreviewTrack(songSelectBeatmap?.Value ?? gameWideBeatmap?.Value);
    }

    public void SilenceMusicController()
    {
        if (musicController == null)
            return;

        try
        {
            bool running = musicController.CurrentTrack.IsRunning;
            if (running)
                musicController.CurrentTrack.Stop();
            setUserPauseRequested(musicController, true);
            Logger.Log(
                $"Uta stopped MusicController on leave wasRunning={running} stillRunning={musicController.CurrentTrack.IsRunning} "
                + $"userPause={musicController.UserPauseRequested}.");
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta MusicController leave stop failed: {exception.Message}");
        }
    }

    public void SeedOriginalVocalsPreference(bool preferred)
    {
        if (originalVocalsPreferenceSeeded)
            return;

        OriginalVocalsPreferred = preferred;
        originalVocalsPreferenceSeeded = true;
    }

    public void RememberOriginalVocals(bool preferred)
    {
        OriginalVocalsPreferred = preferred;
        originalVocalsPreferenceSeeded = true;
    }

    public bool ShouldPlayOriginalVocals(bool fromMods)
    {
        if (fromMods)
            RememberOriginalVocals(true);

        return UtaAudioMath.OriginalVocalsShouldPlay(fromMods, OriginalVocalsPreferred);
    }

    public void PrepareSongSelectPreview()
    {
        if (Sessions.Current != null)
            return;

        // LoadTrack the instance SongSelect.beginLooping will read, not
        // LastPlayed if that is a different WorkingBeatmap (§17 / §24).
        EnsurePreviewTrack(songSelectBeatmap?.Value ?? GameBeatmap?.Value ?? LastPlayedBeatmap);
    }

    private static void setUserPauseRequested(MusicController music, bool value)
    {
        if (user_pause_requested_field != null)
            user_pause_requested_field.SetValue(music, value);
        else
            user_pause_requested?.SetValue(music, value);
    }

    private static void stopTrack(WorkingBeatmap? beatmap, string reason, WorkingBeatmap? incoming = null)
    {
        if (beatmap == null)
            return;
        if (incoming != null && beatmap.BeatmapInfo.ID == incoming.BeatmapInfo.ID)
            return;
        if (!beatmap.TrackLoaded)
            return;

        try
        {
            bool running = beatmap.Track.IsRunning;
            if (running)
                beatmap.Track.Stop();
            bool still = beatmap.Track.IsRunning;
            if (running)
                Logger.Log($"Uta stopped leftover track '{beatmap}' stillRunning={still} ({reason}).");
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta leftover track stop failed ({reason}): {exception.Message}");
        }
    }

    private static void tryEnableBindable(Bindable<WorkingBeatmap>? bindable, string name)
    {
        if (bindable == null || !bindable.Disabled)
            return;

        try
        {
            bindable.Disabled = false;
            Logger.Log($"Uta re-enabled the {name} beatmap bindable.");
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta could not enable the {name} beatmap bindable: {exception.Message}");
        }
    }

    public void EnsureAllPreviewTracks()
        => EnsurePreviewTrack(GameBeatmap?.Value ?? LastPlayedBeatmap);

    public static void EnsurePreviewTrack(WorkingBeatmap? beatmap)
    {
        if (beatmap == null)
            return;

        try
        {
            if (!beatmap.TrackLoaded)
            {
                beatmap.LoadTrack();
                Logger.Log($"Uta loaded preview track for '{beatmap}'.");
            }

            beatmap.PrepareTrackForPreview(true);
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta could not load preview track for '{beatmap}': {exception.Message}");
        }
    }

    /// <summary>
    /// Stop a previous chart's TrackBass when the selection moves on. LoadTrack of
    /// the new chart does not stop the old instance (osu MusicController.Expire
    /// only wraps its own DrawableTrack).
    /// </summary>
    public static void StopLeftoverTrack(WorkingBeatmap? previous, WorkingBeatmap? incoming)
    {
        if (previous == null || incoming == null)
            return;
        if (previous.BeatmapInfo.ID == incoming.BeatmapInfo.ID)
            return;
        if (!previous.TrackLoaded)
            return;

        try
        {
            if (!previous.Track.IsRunning)
                return;

            previous.Track.Stop();
            Logger.Log($"Uta stopped leftover track '{previous}' for '{incoming}'.");
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta leftover track stop failed: {exception.Message}");
        }
    }

    private void broadcastQueue()
        => RemoteServerController.BroadcastQueue(CommandRouter.GetQueueMessage());

    private void onProcessExit(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= onProcessExit;
        if (songSelectBeatmap != null && watchingSongSelectBeatmap)
            songSelectBeatmap.ValueChanged -= onSongSelectBeatmapChanged;
        if (selectedMods != null)
            selectedMods.ValueChanged -= onSelectedModsChanged;
        if (screenStack != null)
        {
            screenStack.ScreenPushed -= onScreenPushed;
            screenStack.ScreenExited -= onScreenExited;
        }
        Queue.Changed -= broadcastQueue;
        RemoteServerController.Dispose();
        Queue.Dispose();
    }
}
