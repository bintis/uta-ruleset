// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Uta.Gameplay;
using osu.Game.Rulesets.Uta.Remote;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.Global.Overlays;

internal sealed partial class UtaRemoteControlOverlay : OsuFocusedOverlayContainer
{
    private readonly UtaRemoteServerController controller;
    private readonly UtaGameplaySessionRegistry sessions;
    private readonly FillFlowContainer<Drawable> content;
    private readonly OsuSpriteText status;
    private readonly OsuSpriteText clients;
    private readonly OsuSpriteText pairing;
    private readonly UtaRemoteQrDisplay qr;
    private readonly SettingsButton startStop;
    private UtaRemoteServer? pairingServer;
    private DateTimeOffset pairingExpiresAt;

    protected override Container<Drawable> Content => content;
    protected override bool DimMainContent => true;
    public override bool BlockScreenWideMouse => true;

    public UtaRemoteControlOverlay(UtaRemoteServerController controller, UtaGameplaySessionRegistry sessions)
    {
        this.controller = controller;
        this.sessions = sessions;
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        Width = 520;
        AutoSizeAxes = Axes.Y;

        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = 12,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(12, 18, 27, 248) },
                content = new FillFlowContainer<Drawable>
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Padding = new MarginPadding(22),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText { Text = "uta! mobile remote", Font = OsuFont.Default.With(size: 23, weight: FontWeight.Bold) },
                        status = line(),
                        clients = line(),
                        pairing = line(),
                        qr = new UtaRemoteQrDisplay { Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre },
                        startStop = new SettingsButton { Text = "Start server", Action = toggleServer },
                        new SettingsButton { Text = "Disconnect all clients", Action = controller.DisconnectAllClients },
                        new SettingsButton { Text = "Close", Action = Hide },
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
        controller.Changed += onChanged;
        sessions.Changed += onChanged;
        refresh();
    }

    public async void ToggleAndEnsureStarted()
    {
        if (State.Value == Visibility.Visible)
        {
            Hide();
            return;
        }
        Show();
        await controller.EnsureStartedAsync(UtaRemoteServerStartReason.RemoteOverlayOpened);
        Schedule(() =>
        {
            ensureControllerPairing();
            refresh();
        });
    }

    private async void toggleServer()
    {
        if (controller.Server?.IsRunning == true)
            await controller.StopAsync(UtaRemoteServerStopReason.ExplicitStop);
        else
        {
            await controller.EnsureStartedAsync(UtaRemoteServerStartReason.ExplicitStart);
            Schedule(ensureControllerPairing);
        }
        Schedule(refresh);
    }

    private void ensureControllerPairing()
    {
        try
        {
            UtaRemoteServer server = controller.Server ?? throw new InvalidOperationException("Server is stopped.");
            if (ReferenceEquals(pairingServer, server) && pairingExpiresAt > DateTimeOffset.UtcNow.AddSeconds(5))
                return;

            UtaRemotePairingTicket ticket = server.CreatePairingTicket(UtaRemoteRole.Controller);
            string url = server.GetPairingUrl(ticket);
            pairingServer = server;
            pairingExpiresAt = ticket.ExpiresAt;
            pairing.Text = $"Controller pairing expires {ticket.ExpiresAt.ToLocalTime():HH:mm:ss}\n{url}";
            qr.SetContent(url);
        }
        catch (Exception exception)
        {
            pairing.Text = exception.GetBaseException().Message;
            qr.SetContent(null);
        }
        refresh();
    }

    private void onChanged() => Schedule(refresh);

    private void refresh()
    {
        UtaRemoteServer? server = controller.Server;
        string countdown = controller.IdleShutdownRemaining.Value is { } remaining ? $" · stops in {Math.Ceiling(remaining.TotalSeconds)}s" : string.Empty;
        status.Text = $"{controller.State.Value}{countdown}" + (controller.LastError.Value == null ? string.Empty : $" · {controller.LastError.Value}");
        clients.Text = $"Authenticated controllers {controller.AuthenticatedClientCount.Value} · gameplay {(sessions.Current == null ? "none" : "active")}";
        bool running = server?.IsRunning == true;
        startStop.Text = running ? "Stop server" : "Start server";
        if (!running)
        {
            pairingServer = null;
            pairingExpiresAt = default;
            pairing.Text = string.Empty;
            qr.SetContent(null);
        }
    }

    protected override void PopIn() => this.FadeIn(180, Easing.OutQuint);
    protected override void PopOut() => this.FadeOut(180, Easing.OutQuint);

    protected override void Dispose(bool isDisposing)
    {
        controller.Changed -= onChanged;
        sessions.Changed -= onChanged;
        base.Dispose(isDisposing);
    }

    private static OsuSpriteText line() => new()
    {
        RelativeSizeAxes = Axes.X,
        AllowMultiline = true,
        Font = OsuFont.Default.With(size: 13),
        Colour = new Color4(196, 221, 215, 255),
    };
}
