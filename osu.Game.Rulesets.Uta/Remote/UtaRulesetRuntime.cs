// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Uta.Gameplay;
using osu.Game.Rulesets.Uta.Library;
using osu.Game.Rulesets.Uta.Playback;
using osu.Game.Rulesets.Uta.Queue;

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
    public Bindable<WorkingBeatmap>? GameBeatmap { get; set; }
    public BeatmapManager? Beatmaps { get; set; }

    public WorkingBeatmap? LastPlayedBeatmap { get; private set; }

    private readonly List<WeakReference<WorkingBeatmap>> previewBeatmaps = new();

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

    public void RememberPlayedBeatmap(WorkingBeatmap beatmap)
    {
        LastPlayedBeatmap = beatmap;
        rememberPreviewCandidate(beatmap);
        tryRememberCurrentGameBeatmap();
        EnsureFreshWorkingTrack(beatmap.BeatmapInfo);
    }

    public void PrepareSongSelectPreview()
    {
        EnsureAllPreviewTracks();

        Bindable<WorkingBeatmap>? game = GameBeatmap;
        WorkingBeatmap? last = LastPlayedBeatmap;
        if (game == null || last == null)
            return;

        try
        {
            if (!game.Disabled && !ReferenceEquals(game.Value, last))
                game.Value = last;
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta could not restore song select beatmap: {exception.Message}");
        }

        try
        {
            EnsurePreviewTrack(game.Disabled ? null : game.Value);
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta could not read song select beatmap: {exception.Message}");
        }

        EnsureFreshWorkingTrack(last.BeatmapInfo);
    }

    public void EnsureAllPreviewTracks()
    {
        EnsurePreviewTrack(LastPlayedBeatmap);
        tryRememberCurrentGameBeatmap();

        for (int i = previewBeatmaps.Count - 1; i >= 0; i--)
        {
            if (!previewBeatmaps[i].TryGetTarget(out WorkingBeatmap? beatmap))
            {
                previewBeatmaps.RemoveAt(i);
                continue;
            }

            EnsurePreviewTrack(beatmap);
            EnsureFreshWorkingTrack(beatmap.BeatmapInfo);
        }
    }

    private void tryRememberCurrentGameBeatmap()
    {
        try
        {
            WorkingBeatmap? current = GameBeatmap?.Value;
            if (current != null)
                rememberPreviewCandidate(current);
        }
        catch
        {
        }
    }

    private void EnsureFreshWorkingTrack(osu.Game.Beatmaps.BeatmapInfo? info)
    {
        if (info == null || Beatmaps == null)
            return;

        try
        {
            EnsurePreviewTrack(Beatmaps.GetWorkingBeatmap(info));
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta could not refresh preview track for '{info}': {exception.Message}");
        }
    }

    private void rememberPreviewCandidate(WorkingBeatmap beatmap)
    {
        foreach (WeakReference<WorkingBeatmap> candidate in previewBeatmaps)
        {
            if (candidate.TryGetTarget(out WorkingBeatmap? existing) && ReferenceEquals(existing, beatmap))
                return;
        }

        previewBeatmaps.Add(new WeakReference<WorkingBeatmap>(beatmap));
    }

    public static void EnsurePreviewTrack(WorkingBeatmap? beatmap)
    {
        if (beatmap == null || beatmap.TrackLoaded)
            return;

        try
        {
            beatmap.LoadTrack();
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta could not load preview track for '{beatmap}': {exception.Message}");
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
        Queue.Changed -= broadcastQueue;
        RemoteServerController.Dispose();
        Queue.Dispose();
    }
}
