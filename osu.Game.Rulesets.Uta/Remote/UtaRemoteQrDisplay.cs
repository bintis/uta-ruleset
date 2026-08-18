// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Net.Codecrete.QrCodeGenerator;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.Remote;

/// <summary>
/// Renders a pairing URL as a scannable QR code so a phone camera can open the mobile remote
/// page directly, instead of the user having to type the address by hand. The symbol is built
/// once per pairing ticket (a handful of drawables, generated on a button press) rather than on
/// every frame, so it carries no gameplay-rendering cost.
/// </summary>
internal sealed partial class UtaRemoteQrDisplay : CompositeDrawable
{
    private const float display_size = 176;
    private const int quiet_zone_modules = 4;

    private readonly Container moduleContainer;

    public UtaRemoteQrDisplay()
    {
        Width = display_size;
        Height = display_size;
        Alpha = 0;
        Masking = true;
        CornerRadius = 6;

        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
            },
            moduleContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
            },
        };
    }

    /// <summary>
    /// Encodes <paramref name="content"/> as a QR code and displays it. Passing null or an empty
    /// string hides the display and releases the previously generated modules.
    /// </summary>
    public void SetContent(string? content)
    {
        moduleContainer.Clear();

        if (string.IsNullOrEmpty(content))
        {
            this.FadeTo(0, 150, Easing.OutQuint);
            return;
        }

        QrCode qr;

        try
        {
            qr = QrCode.EncodeText(content, QrCode.Ecc.Medium);
        }
        catch (Exception)
        {
            // Pairing links are short and well within QR capacity; if encoding ever fails there is
            // nothing actionable for the player beyond falling back to the plain-text link.
            this.FadeTo(0, 150, Easing.OutQuint);
            return;
        }

        int dimension = qr.Size + quiet_zone_modules * 2;
        float moduleSize = 1f / dimension;

        var modules = new List<Drawable>();

        for (int y = 0; y < qr.Size; y++)
        {
            for (int x = 0; x < qr.Size; x++)
            {
                if (!qr.GetModule(x, y))
                    continue;

                modules.Add(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    RelativePositionAxes = Axes.Both,
                    Colour = Color4.Black,
                    Position = new Vector2((x + quiet_zone_modules) * moduleSize, (y + quiet_zone_modules) * moduleSize),
                    Size = new Vector2(moduleSize),
                });
            }
        }

        moduleContainer.Children = modules;
        this.FadeTo(1, 150, Easing.OutQuint);
    }
}
