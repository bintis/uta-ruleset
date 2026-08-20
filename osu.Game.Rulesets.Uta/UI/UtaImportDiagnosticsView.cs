// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Uta.Import;
using osu.Game.Rulesets.Uta.Localisation;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

internal sealed partial class UtaImportDiagnosticsView : FillFlowContainer<Drawable>
{
    private readonly OsuSpriteText title;
    private readonly OsuSpriteText content;
    private readonly SettingsButton refreshButton;
    private readonly SettingsButton clearButton;
    private readonly Bindable<string> locale = new();
    private UtaUiLanguage language;

    public UtaImportDiagnosticsView()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FillDirection.Vertical;
        Spacing = new Vector2(0, 5);
        Children = new Drawable[]
        {
            title = new OsuSpriteText
            {
                Font = OsuFont.Default.With(size: 15, weight: FontWeight.Bold),
            },
            content = new OsuSpriteText
            {
                RelativeSizeAxes = Axes.X,
                AllowMultiline = true,
                Font = OsuFont.Default.With(size: 12),
                Colour = new Color4(190, 195, 216, 255),
            },
            refreshButton = new SettingsButton
            {
                Action = refresh,
            },
            clearButton = new SettingsButton
            {
                Action = () =>
                {
                    UtaImportDiagnostics.Clear();
                    refresh();
                },
            },
        };
    }

    [BackgroundDependencyLoader]
    private void load(FrameworkConfigManager frameworkConfig)
    {
        locale.BindTo(frameworkConfig.GetBindable<string>(FrameworkSetting.Locale));
        locale.BindValueChanged(value =>
        {
            language = UtaLanguageResolver.FromLocale(value.NewValue);
            refreshLabels();
        }, true);
    }

    private void refreshLabels()
    {
        title.Text = UtaStrings.Get("import.title", language);
        refreshButton.Text = UtaStrings.Get("import.refresh", language);
        clearButton.Text = UtaStrings.Get("import.clear", language);
        refresh();
    }

    private void refresh()
    {
        UtaImportDiagnostic[] items = UtaImportDiagnostics.Snapshot().Take(8).ToArray();
        content.Text = items.Length == 0
            ? UtaStrings.Get("import.none", language)
            : string.Join("\n", items.Select(item => $"{item.Timestamp:HH:mm:ss}  {item.FileName}  [{item.Category}]  {item.Message}"));
    }

    protected override void Dispose(bool isDisposing)
    {
        locale.UnbindAll();
        base.Dispose(isDisposing);
    }
}
