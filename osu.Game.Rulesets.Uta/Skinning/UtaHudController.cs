// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Linq;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Uta.Skinning;

public sealed class UtaSkinTransformer : SkinTransformer
{
    public UtaSkinTransformer(ISkin skin)
        : base(skin)
    {
    }

    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        // Ignore persisted ruleset HUD layouts. Uta supplies only its controller
        // here; lazer still loads the global skin HUD in a separate container.
        if (lookup.GetType().Name == "UserSkinComponentLookup"
            && lookup.GetType().GetField("Component", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(lookup)
            is GlobalSkinnableContainerLookup
            {
                Lookup: GlobalSkinnableContainers.MainHUDComponents,
                Ruleset: not null,
            })
            return null;

        if (lookup is GlobalSkinnableContainerLookup
            {
                Lookup: GlobalSkinnableContainers.MainHUDComponents,
                Ruleset: not null,
            })
        {
            return new Container
            {
                RelativeSizeAxes = Axes.Both,
                Child = new UtaHudController(),
            };
        }

        return base.GetDrawableComponent(lookup);
    }
}

/// <summary>
/// Suppresses global gameplay statistics which have no useful Uta meaning.
/// </summary>
internal sealed partial class UtaHudController : CompositeDrawable, ISerialisableDrawable
{
    private SkinnableContainer? globalHudComponents;
    private HUDOverlay? hudOverlay;
    private bool reported;

    public bool UsesFixedAnchor { get; set; }

    public UtaHudController()
    {
        AlwaysPresent = true;
        Size = default;
    }

    [BackgroundDependencyLoader]
    private void load(HUDOverlay hud)
    {
        hudOverlay = hud;
        globalHudComponents = hud.Children.OfType<SkinnableContainer>()
                                 .FirstOrDefault(container => container.Lookup.Lookup == GlobalSkinnableContainers.MainHUDComponents
                                                              && container.Lookup.Ruleset == null);
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();
        if (globalHudComponents == null)
            return;

        int suppressed = 0;
        foreach (ISerialisableDrawable component in globalHudComponents.Components)
        {
            if (component is SongProgress)
                continue;

            if (component is Drawable drawable)
            {
                drawable.Alpha = 0;
                suppressed++;
            }
        }

        if (hudOverlay != null)
            hudOverlay.TopRightElements.Alpha = 0;

        if (!reported && globalHudComponents.ComponentsLoaded)
        {
            Logger.Log($"Uta global HUD filter active: suppressed {suppressed} component(s), preserving song progress.");
            reported = true;
        }
    }
}
