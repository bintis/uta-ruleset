// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Uta.Playback;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.Remote;

/// <summary>
/// Starts the mobile remote for an immersive-queue session and briefly offers controller
/// pairing when no authenticated phone is connected. Non-IQ gameplay never creates this
/// component and therefore does not start the server automatically.
/// </summary>
internal sealed partial class UtaImmersiveRemotePrompt : CompositeDrawable
{
    private readonly UtaRemoteServerController controller;
    private readonly UtaPlaybackCoordinator playback;
    private readonly UtaRemoteQrDisplay qr;
    private readonly OsuSpriteText status;

    public UtaImmersiveRemotePrompt(UtaRemoteServerController controller, UtaPlaybackCoordinator playback)
    {
        this.controller = controller;
        this.playback = playback;
        AlwaysPresent = true;
        Anchor = Anchor.CentreRight;
        Origin = Anchor.CentreRight;
        Position = new Vector2(-36, 0);
        Size = new Vector2(224, 232);
        Alpha = 0;

        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(12, 18, 27, 235),
            },
            qr = new UtaRemoteQrDisplay
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = 12,
            },
            status = new OsuSpriteText
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Y = -12,
                Text = "Scan to control this queue",
                Font = OsuFont.Default.With(size: 14, weight: FontWeight.Bold),
                Colour = new Color4(220, 238, 232, 255),
            },
        };
        Masking = true;
        CornerRadius = 12;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        controller.Changed += onControllerChanged;
        Schedule(() =>
        {
            if (playback.IsImmersiveQueueEnabled)
                _ = startAndShowAsync();
            else
                Logger.Log("Uta IQ remote pairing prompt disabled: IQ is not selected.");
        });
    }

    private async Task startAndShowAsync()
    {
        await controller.EnsureStartedAsync(UtaRemoteServerStartReason.ImmersiveQueueGameplayStarted);
        Schedule(showIfPairingIsNeeded);
    }

    private void showIfPairingIsNeeded()
    {
        if (controller.AuthenticatedClientCount.Value > 0)
        {
            Logger.Log("Uta IQ remote pairing prompt skipped: a controller is already connected.");
            return;
        }

        try
        {
            UtaRemoteServer server = controller.Server ?? throw new InvalidOperationException("Remote server did not start.");
            UtaRemotePairingTicket ticket = server.CreatePairingTicket(UtaRemoteRole.Controller);
            qr.SetContent(server.GetPairingUrl(ticket));
            status.Text = "Scan to control this queue";
            this.FadeIn(180, Easing.OutQuint);
            Logger.Log("Uta IQ remote pairing prompt shown.");
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta IQ remote pairing prompt failed: {exception.GetBaseException().Message}", level: LogLevel.Error);
        }
    }

    private void onControllerChanged()
    {
        if (controller.AuthenticatedClientCount.Value > 0)
            Schedule(() =>
            {
                this.FadeOut(180, Easing.OutQuint);
                qr.SetContent(null);
            });
    }

    protected override void Dispose(bool isDisposing)
    {
        controller.Changed -= onControllerChanged;
        base.Dispose(isDisposing);
    }
}
