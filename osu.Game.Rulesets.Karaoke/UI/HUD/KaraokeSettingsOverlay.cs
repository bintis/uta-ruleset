// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays;
using osu.Game.Rulesets.Karaoke.Configuration;
using osu.Game.Rulesets.Karaoke.UI.PlayerSettings;
using osu.Game.Screens.Play.PlayerSettings;
using osuTK;

namespace osu.Game.Rulesets.Karaoke.UI.HUD;

/// <summary>
/// The same native quick-settings card stack shown by lazer before gameplay,
/// made available in-game with karaoke-specific controls appended.
/// </summary>
public partial class KaraokeSettingsOverlay : OsuFocusedOverlayContainer
{
    private const float padding = 20;

    protected override bool DimMainContent => false;

    protected override Container<Drawable> Content => groups;

    private readonly FillFlowContainer<Drawable> groups;
    private readonly KaraokeRulesetConfigManager config;

    [Cached]
    private readonly OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    public KaraokeSettingsOverlay(KaraokeRulesetConfigManager config)
    {
        this.config = config;

        Anchor = Anchor.TopRight;
        Origin = Anchor.TopRight;
        RelativeSizeAxes = Axes.Y;
        Width = SettingsToolboxGroup.CONTAINER_WIDTH + padding * 2;

        InternalChild = new OsuScrollContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = groups = new FillFlowContainer<Drawable>
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 20),
                Padding = new MarginPadding(padding),
                Children = new Drawable[]
                {
                    new VisualSettings(),
                    new AudioSettings(),
                    new InputSettings(),
                    new UtaAudioSettings(),
                },
            },
        };
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        dependencies.CacheAs(config);
        return dependencies;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        OverlayActivationMode.UnbindAll();
        ((Bindable<OverlayActivation>)OverlayActivationMode).Value = OverlayActivation.All;
    }

    protected override void PopIn()
    {
        this.MoveToX(0, 400, Easing.OutQuint);
        this.FadeIn(200, Easing.OutQuint);
    }

    protected override void PopOut()
    {
        base.PopOut();
        this.MoveToX(DrawWidth, 400, Easing.OutQuint);
        this.FadeOut(200, Easing.OutQuint);
    }
}
