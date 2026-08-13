// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Karaoke.UI.HUD;

public partial class SettingButtonsDisplay : CompositeDrawable, ISerialisableDrawable
{
    private SkinnableContainer? globalHudComponents;
    private HUDOverlay? hudOverlay;

    public bool UsesFixedAnchor { get; set; }

    public SettingButtonsDisplay()
    {
        AlwaysPresent = true;
        Size = default;
    }

    private SettingOverlayContainer? settingOverlayContainer;

    [BackgroundDependencyLoader]
    private void load(HUDOverlay hud, Player player)
    {
        hudOverlay = hud;
        globalHudComponents = hud.Children.OfType<SkinnableContainer>()
                                 .FirstOrDefault(container => container.Lookup.Lookup == GlobalSkinnableContainers.MainHUDComponents
                                                              && container.Lookup.Ruleset == null);

        var rulesetInfo = player.Ruleset.Value;
        Schedule(() =>
        {
            hud.Add(new KaraokeControlInputManager(rulesetInfo)
            {
                RelativeSizeAxes = Axes.Both,
                Child = settingOverlayContainer = new SettingOverlayContainer
                {
                    RelativeSizeAxes = Axes.Both,
                },
            });
        });
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        // lazer loads global skin HUD pieces alongside the ruleset-specific HUD.
        // They contain the leaderboard/health/score and duplicate timing meters
        // which the focused Uta layout deliberately does not use.
        if (globalHudComponents != null)
        {
            foreach (var component in globalHudComponents.Components)
            {
                // Keep lazer's unobtrusive bottom song progress indicator.
                if (component.GetType().Name.Contains("SongProgress"))
                    continue;

                if (component is Drawable drawable)
                    drawable.Alpha = 0;
            }
        }

        if (hudOverlay != null)
            hudOverlay.TopRightElements.Alpha = 0;
    }
}
