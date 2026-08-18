// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Testing;
using osu.Game;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Volume;
using osu.Game.Rulesets.Uta.Library;
using osu.Game.Rulesets.Uta.Playback;
using osu.Game.Rulesets.Uta.Queue;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.Global.Overlays;

internal sealed partial class UtaQueueOverlay : OsuFocusedOverlayContainer
{
    private readonly UtaSongQueueService queue;
    private readonly UtaPlaybackCoordinator playback;
    private readonly UtaSongLibrary library;
    private readonly FillFlowContainer<Drawable> content;
    private readonly FillFlowContainer<Drawable> entries;
    private readonly FillFlowContainer<Drawable> searchResults;
    private readonly OsuScrollContainer searchScroll;
    private readonly BasicSearchTextBox search;
    private readonly OsuSpriteText status;
    private readonly RoundedButton addSongsButton;
    private VolumeOverlay? volumeOverlay;
    private bool browsingSongs;

    protected override Container<Drawable> Content => content;
    protected override bool DimMainContent => true;
    public override bool BlockScreenWideMouse => true;

    public UtaQueueOverlay(UtaSongQueueService queue, UtaPlaybackCoordinator playback, UtaSongLibrary library)
    {
        this.queue = queue;
        this.playback = playback;
        this.library = library;
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Y;
        Width = 620;

        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Width = 1,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(14, 20, 24, 248) },
                content = new FillFlowContainer<Drawable>
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Padding = new MarginPadding(24),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText { Text = "uta! global queue", Font = OsuFont.Default.With(size: 24, weight: FontWeight.Bold) },
                        status = textLine(),
                        new FillFlowContainer<Drawable>
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(6, 0),
                            Children = new Drawable[]
                            {
                                actionButton("Play next", 90, playNext),
                                actionButton("End song", 90, () => playback.RequestEndCurrent()),
                                addSongsButton = actionButton("Add songs", 90, toggleSongBrowser),
                                actionButton("Clear queue", 100, () => queue.Clear()),
                                actionButton("Close", 70, Hide),
                            },
                        },
                        search = new BasicSearchTextBox
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 36,
                            PlaceholderText = "Search Uta songs to add...",
                            ReleaseFocusOnCommit = false,
                            Alpha = 0,
                        },
                        searchScroll = new OsuScrollContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 500,
                            Alpha = 0,
                            Child = searchResults = new FillFlowContainer<Drawable>
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 4),
                            },
                        },
                        entries = new FillFlowContainer<Drawable>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 5),
                        },
                    },
                },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        OverlayActivationMode.UnbindAll();
        ((Bindable<OverlayActivation>)OverlayActivationMode).Value = OverlayActivation.All;
        queue.Changed += onQueueChanged;
        playback.TransitionState.BindValueChanged(_ => refresh());
        search.Current.BindValueChanged(_ => refreshSearch());
        volumeOverlay = this.FindClosestParent<OsuGame>()?.ChildrenOfType<VolumeOverlay>().FirstOrDefault();
        refresh();
    }

    protected override void Update()
    {
        base.Update();

        if (State.Value == Visibility.Visible)
            volumeOverlay?.Hide();
    }

    private void playNext()
    {
        QueueMutationResult result = playback.RequestSkipToNext();
        if (result.Succeeded)
            Hide();
        else if (result.Error == "The queue is empty.")
            status.Text = "The Uta queue is empty.";
    }

    private void onQueueChanged() => Schedule(refresh);

    private void toggleSongBrowser() => setBrowsingSongs(!browsingSongs);

    private void setBrowsingSongs(bool browsing)
    {
        browsingSongs = browsing;
        addSongsButton.Text = browsing ? "Queue" : "Add songs";

        if (browsing)
        {
            entries.Hide();
            search.Show();
            searchScroll.Show();
            refreshSearch();
        }
        else
        {
            search.Hide();
            searchScroll.Hide();
            entries.Show();
        }
    }

    private void refreshSearch()
    {
        searchResults.Clear();
        IReadOnlyList<UtaSongLibraryEntry> songs = library.Browse(search.Current.Value);

        foreach (UtaSongLibraryEntry song in songs)
        {
            searchResults.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 36,
                Masking = true,
                CornerRadius = 6,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(20, 29, 34, 245),
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = 10, Right = 70 },
                        Child = new TruncatingSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            Text = $"{song.Title} · {song.Artist} · {song.DifficultyName}",
                            Font = OsuFont.Default.With(size: 14),
                        },
                    },
                    actionButton("Add", 54, () => addSong(song)).With(button =>
                    {
                        button.Anchor = Anchor.CentreRight;
                        button.Origin = Anchor.CentreRight;
                        button.X = -5;
                    }),
                },
            });
        }
    }

    private void addSong(UtaSongLibraryEntry song)
        => queue.Add(new UtaSongRequest(
            song.BeatmapId,
            song.Title,
            song.Artist,
            song.DifficultyName,
            song.LengthMs,
            UtaQueueRequestSource.LocalOverlay));

    private void refresh()
    {
        UtaSongQueueEntry[] snapshot = queue.GetSnapshot().ToArray();
        status.Text = $"{snapshot.Length} song(s) · revision {queue.Revision.Value} · {playback.TransitionState.Value}";
        entries.Clear();
        for (int i = 0; i < snapshot.Length; i++)
        {
            UtaSongQueueEntry entry = snapshot[i];
            var row = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 40,
                Masking = true,
                CornerRadius = 6,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(24, 34, 39, 255),
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = 10, Right = 330 },
                        Child = new TruncatingSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            Text = $"{i + 1}. {entry.Title} · {entry.Artist} [{entry.DifficultyName}] · {entry.State}",
                            Font = OsuFont.Default.With(size: 13, weight: FontWeight.SemiBold),
                        },
                    },
                    new FillFlowContainer<Drawable>
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -5,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(4, 0),
                        Children = new Drawable[]
                        {
                            actionButton("Play", 50, () => playback.RequestPlayNow(entry.EntryId)),
                            actionButton("Top", 42, () => queue.MoveToTop(entry.EntryId)),
                            actionButton("Up", 38, () => queue.Move(entry.EntryId, Math.Max(0, queue.GetSnapshot().ToList().FindIndex(item => item.EntryId == entry.EntryId) - 1))),
                            actionButton("Down", 48, () => queue.Move(entry.EntryId, queue.GetSnapshot().ToList().FindIndex(item => item.EntryId == entry.EntryId) + 1)),
                            actionButton("Bottom", 58, () => queue.MoveToBottom(entry.EntryId)),
                            actionButton("Remove", 62, () => queue.Remove(entry.EntryId)),
                        },
                    },
                },
            };
            entries.Add(row);
        }
    }

    private static RoundedButton actionButton(string text, float width, Action action) => new()
    {
        RelativeSizeAxes = Axes.None,
        Size = new Vector2(width, 30),
        Text = text,
        Action = action,
    };

    protected override bool OnScroll(ScrollEvent e) => true;

    protected override void PopIn()
    {
        search.Current.Value = string.Empty;
        setBrowsingSongs(false);
        this.FadeIn(180, Easing.OutQuint);
    }
    protected override void PopOut() => this.FadeOut(180, Easing.OutQuint);

    protected override void Dispose(bool isDisposing)
    {
        queue.Changed -= onQueueChanged;
        base.Dispose(isDisposing);
    }

    private static OsuSpriteText textLine() => new()
    {
        RelativeSizeAxes = Axes.X,
        AllowMultiline = true,
        Font = OsuFont.Default.With(size: 13),
        Colour = new Color4(196, 221, 215, 255),
    };
}
