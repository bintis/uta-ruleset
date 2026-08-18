// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Linq;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.Skinning;

public sealed class UtaSkinTransformer : SkinTransformer
{
    public UtaSkinTransformer(ISkin skin)
        : base(skin)
    {
    }

    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (lookup is UtaSkinComponentLookup utaLookup)
        {
            Drawable? supplied = base.GetDrawableComponent(lookup);
            return supplied ?? createFallback(utaLookup.Component);
        }

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

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        IBindable<TValue>? supplied = base.GetConfig<TLookup, TValue>(lookup);
        if (supplied != null || lookup is not UtaSkinConfigurationLookup uta)
            return supplied;

        return uta.Lookup switch
        {
            UtaSkinConfiguration.GridColour => SkinUtils.As<TValue>(new Bindable<Color4>(UtaAccessiblePalette.Grid)),
            UtaSkinConfiguration.TargetColour => SkinUtils.As<TValue>(new Bindable<Color4>(UtaAccessiblePalette.Target)),
            UtaSkinConfiguration.SongCurveColour => SkinUtils.As<TValue>(new Bindable<Color4>(UtaAccessiblePalette.SongCurve)),
            UtaSkinConfiguration.LiveCurveColour => SkinUtils.As<TValue>(new Bindable<Color4>(UtaAccessiblePalette.LiveCurve)),
            UtaSkinConfiguration.PlayheadColour => SkinUtils.As<TValue>(new Bindable<Color4>(UtaAccessiblePalette.Playhead)),
            UtaSkinConfiguration.GoodFeedbackColour => SkinUtils.As<TValue>(new Bindable<Color4>(UtaAccessiblePalette.Good)),
            UtaSkinConfiguration.BadFeedbackColour => SkinUtils.As<TValue>(new Bindable<Color4>(UtaAccessiblePalette.Bad)),
            UtaSkinConfiguration.LineWeight => SkinUtils.As<TValue>(new BindableFloat(2.5f) { MinValue = 1, MaxValue = 8 }),
            UtaSkinConfiguration.NoteSpacing => SkinUtils.As<TValue>(new BindableFloat(1) { MinValue = 0.6f, MaxValue = 1.8f }),
            UtaSkinConfiguration.AnimationIntensity => SkinUtils.As<TValue>(new BindableFloat(0.65f) { MinValue = 0, MaxValue = 1 }),
            _ => null,
        };
    }

    private static Drawable createFallback(UtaSkinComponents component)
    {
        Color4 colour = component switch
        {
            UtaSkinComponents.Grid => UtaAccessiblePalette.Grid,
            UtaSkinComponents.TargetNote => UtaAccessiblePalette.Target,
            UtaSkinComponents.SongPitchCurve => UtaAccessiblePalette.SongCurve,
            UtaSkinComponents.LivePitchCurve => UtaAccessiblePalette.LiveCurve,
            UtaSkinComponents.Playhead => UtaAccessiblePalette.Playhead,
            UtaSkinComponents.ScoringFeedback => UtaAccessiblePalette.Good,
            UtaSkinComponents.SingingParticle => UtaAccessiblePalette.LiveCurve,
            _ => UtaAccessiblePalette.Background,
        };

        return new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = colour,
        };
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
