// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Uta.Import;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

internal sealed partial class UtaImportDiagnosticsView : FillFlowContainer<Drawable>
{
    private readonly OsuSpriteText content;

    public UtaImportDiagnosticsView()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FillDirection.Vertical;
        Spacing = new Vector2(0, 5);
        Children = new Drawable[]
        {
            new OsuSpriteText
            {
                Text = "Recent .utz import diagnostics",
                Font = OsuFont.Default.With(size: 15, weight: FontWeight.Bold),
            },
            content = new OsuSpriteText
            {
                RelativeSizeAxes = Axes.X,
                AllowMultiline = true,
                Font = OsuFont.Default.With(size: 12),
                Colour = new Color4(190, 195, 216, 255),
            },
            new SettingsButton
            {
                Text = "Refresh import diagnostics",
                Action = refresh,
            },
            new SettingsButton
            {
                Text = "Clear import diagnostics",
                Action = () =>
                {
                    UtaImportDiagnostics.Clear();
                    refresh();
                },
            },
        };
        refresh();
    }

    private void refresh()
    {
        UtaImportDiagnostic[] items = UtaImportDiagnostics.Snapshot().Take(8).ToArray();
        content.Text = items.Length == 0
            ? "No failed .utz imports have been recorded in this process."
            : string.Join("\n", items.Select(item => $"{item.Timestamp:HH:mm:ss}  {item.FileName}  [{item.Category}]  {item.Message}"));
    }
}
