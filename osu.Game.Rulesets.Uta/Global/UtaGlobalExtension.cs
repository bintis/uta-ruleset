// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Gameplay;
using osu.Game.Rulesets.Uta.Global.Overlays;
using osu.Game.Rulesets.Uta.Library;
using osu.Game.Rulesets.Uta.Playback;
using osu.Game.Rulesets.Uta.Queue;
using osu.Game.Rulesets.Uta.Remote;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.Global;

public sealed partial class UtaGlobalExtension : CompositeDrawable, IKeyBindingHandler<UtaAction>
{
    private readonly UtaSongLibrary library;
    private readonly UtaSongQueueService queue;
    private readonly UtaRemoteServerController remoteServerController;
    private readonly UtaQueueOverlay queueOverlay;
    private readonly UtaRemoteControlOverlay remoteOverlay;
    private readonly UtaGameplayToast toast;
    private readonly UtaRulesetRuntime runtime;
    private IDisposable? gameplayServicesLease;
    private double lastEmptyQueueToast = double.NegativeInfinity;

    public UtaSongQueueService SongQueue => queue;
    public UtaSongLibrary SongLibrary => library;
    public UtaPlaybackCoordinator Playback { get; }
    public UtaGameplaySessionRegistry GameplaySessions { get; }
    public UtaRemoteServerController RemoteServerController => remoteServerController;

    public UtaGlobalExtension()
    {
        RelativeSizeAxes = Axes.Both;
        runtime = UtaRulesetRuntime.Instance;
        queue = runtime.Queue;
        library = new UtaSongLibrary();
        GameplaySessions = runtime.Sessions;
        Playback = new UtaPlaybackCoordinator(queue, library, GameplaySessions, runtime.AutoAdvanceEnabled);
        remoteServerController = runtime.RemoteServerController;
        queueOverlay = new UtaQueueOverlay(queue, Playback, library);
        remoteOverlay = new UtaRemoteControlOverlay(remoteServerController, GameplaySessions);
        toast = new UtaGameplayToast();

        InternalChildren = new Drawable[]
        {
            library,
            Playback,
            queueOverlay,
            remoteOverlay,
            toast,
        };
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        dependencies.CacheAs(queue);
        dependencies.CacheAs(library);
        dependencies.CacheAs(Playback);
        dependencies.CacheAs(GameplaySessions);
        dependencies.CacheAs(remoteServerController);
        return dependencies;
    }

    [BackgroundDependencyLoader(true)]
    private void load(osu.Game.Overlays.MusicController? music)
    {
        // Injecting Bindable<WorkingBeatmap> here yields PlayerLoader's lease,
        // not the copy SongSelect.OnResuming still reads.
        runtime.AttachMusicController(music);
        runtime.AttachSelectedMods(findSelectedMods());
        runtime.AttachSongSelectBeatmap(findSongSelectBeatmap(), findGameWideBeatmap());
        hookScreenExit();
    }

    private Bindable<IReadOnlyList<Mod>>? findSelectedMods()
    {
        var osuGame = this.FindClosestParent<OsuGame>();
        if (osuGame == null)
            return null;

        FieldInfo? field = typeof(OsuGameBase).GetField("SelectedMods", BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(osuGame) as Bindable<IReadOnlyList<Mod>>;
    }

    private Bindable<WorkingBeatmap>? findGameWideBeatmap()
    {
        var osuGame = this.FindClosestParent<OsuGame>();
        if (osuGame == null)
            return null;

        PropertyInfo? property = typeof(OsuGameBase).GetProperty("Beatmap", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return property?.GetValue(osuGame) as Bindable<WorkingBeatmap>;
    }

    private Bindable<WorkingBeatmap>? findSongSelectBeatmap()
    {
        IScreen? cursor = this.FindClosestParent<Player>();
        while (cursor != null)
        {
            if (cursor is SongSelect songSelect)
                return songSelect.Beatmap;

            cursor = cursor.GetParentScreen();
        }

        var osuGame = this.FindClosestParent<OsuGame>();
        if (osuGame == null)
            return null;

        SongSelect? fromTree = osuGame.ChildrenOfType<SongSelect>().LastOrDefault();
        if (fromTree != null)
            return fromTree.Beatmap;

        cursor = osuGame.ScreenStack.CurrentScreen;
        while (cursor != null)
        {
            if (cursor is SongSelect songSelect)
                return songSelect.Beatmap;

            cursor = cursor.GetParentScreen();
        }

        PropertyInfo? property = typeof(OsuGameBase).GetProperty("Beatmap", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        return property?.GetValue(osuGame) as Bindable<WorkingBeatmap>;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        // F7 lets PlayerLoader / ScreenStack UnbindAll return the lease.
        // Returning it from Uta unbinds SongSelect from game-wide (§23 / §27).
        runtime.AttachSelectedMods(findSelectedMods());
        runtime.AttachSongSelectBeatmap(findSongSelectBeatmap(), findGameWideBeatmap());
        hookScreenExit();
        gameplayServicesLease = runtime.AttachGameplayServices(library, Playback);
    }

    private void hookScreenExit()
    {
        var osuGame = this.FindClosestParent<OsuGame>();
        if (osuGame == null)
            return;

        runtime.HookScreenExit(osuGame.ScreenStack);
    }

    public bool OnPressed(KeyBindingPressEvent<UtaAction> e)
    {
        if (e.Repeat)
            return false;

        switch (e.Action)
        {
            case UtaAction.ToggleQueueOverlay:
                {
                    QueueMutationResult result = Playback.RequestSkipToNext();
                    if (!result.Succeeded)
                    {
                        osu.Framework.Logging.Logger.Log($"Uta next-song hotkey rejected: {result.Error}");
                        showEmptyQueueFeedback(result.Error);
                    }

                    return true;
                }

            case UtaAction.OpenQueueOverlay:
                queueOverlay.ToggleVisibility();
                return true;

            case UtaAction.ToggleRemoteOverlay:
                remoteOverlay.ToggleAndEnsureStarted();
                return true;

            default:
                return false;
        }
    }

    public void OnReleased(KeyBindingReleaseEvent<UtaAction> e)
    {
    }

    private void showEmptyQueueFeedback(string? error)
    {
        // Gameplay hides the global notification overlay, so SimpleNotification
        // piles up and dumps after the player exits. Show a local toast instead,
        // and only reopen the queue overlay once per burst of next-song presses.
        if (error == "transition_busy")
            return;

        string text = error == "The queue is empty."
            ? "The Uta queue is empty."
            : (error ?? "Could not start the next queued song.");

        if (Time.Current - lastEmptyQueueToast < 2000)
            return;

        lastEmptyQueueToast = Time.Current;
        toast.Show(text);
        queueOverlay.Show();
    }

    protected override void Dispose(bool isDisposing)
    {
        gameplayServicesLease?.Dispose();
        base.Dispose(isDisposing);
    }

    private sealed partial class UtaGameplayToast : CompositeDrawable
    {
        private readonly OsuSpriteText label;

        public UtaGameplayToast()
        {
            Anchor = Anchor.TopCentre;
            Origin = Anchor.TopCentre;
            Y = 80;
            AutoSizeAxes = Axes.Both;
            Alpha = 0;
            InternalChild = new CircularContainer
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(14, 20, 24, 230),
                    },
                    label = new OsuSpriteText
                    {
                        Margin = new MarginPadding { Horizontal = 18, Vertical = 10 },
                        Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                    },
                },
            };
        }

        public void Show(string text)
        {
            label.Text = text;
            this.FadeIn(120, Easing.OutQuint)
                .Delay(1800)
                .FadeOut(220, Easing.OutQuint);
        }
    }
}
