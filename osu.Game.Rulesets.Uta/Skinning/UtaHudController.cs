// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Linq;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;
using osu.Game.Rulesets.Uta.Skinning.Lookups;
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

        if (lookup is UtaTargetNoteLookup or UtaCurveLookup or UtaGridLookup or UtaLyricsDecorationLookup or UtaScoringFeedbackLookup)
        {
            Drawable? supplied = base.GetDrawableComponent(lookup);
            return supplied ?? createFallback(componentFor(lookup));
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

    public override Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
    {
        if (!UtaSkinAssetNames.IsKnown(componentName) || componentName == UtaSkinAssetNames.Marker)
            return base.GetTexture(componentName, wrapModeS, wrapModeT);

        // A marker prevents unrelated files in a normal osu! skin from being interpreted as
        // a complete uta! skin. LegacySkin performs @2x selection inside this lookup.
        if (base.GetTexture(UtaSkinAssetNames.Marker, wrapModeS, wrapModeT) == null)
            return null;

        return base.GetTexture(componentName, wrapModeS, wrapModeT);
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
            UtaSkinConfiguration.SurfaceColour => SkinUtils.As<TValue>(new Bindable<Color4>(new Color4(11, 16, 32, 255))),
            UtaSkinConfiguration.GridMajorWeight => SkinUtils.As<TValue>(new BindableFloat(1.25f) { MinValue = 0.5f, MaxValue = 4 }),
            UtaSkinConfiguration.GridMinorWeight => SkinUtils.As<TValue>(new BindableFloat(0.75f) { MinValue = 0.25f, MaxValue = 3 }),
            UtaSkinConfiguration.ReferenceCurveWeight => SkinUtils.As<TValue>(new BindableFloat(2.25f) { MinValue = 1, MaxValue = 8 }),
            UtaSkinConfiguration.LiveCurveWeight => SkinUtils.As<TValue>(new BindableFloat(3.25f) { MinValue = 1.5f, MaxValue = 10 }),
            UtaSkinConfiguration.TargetNoteHeight => SkinUtils.As<TValue>(new BindableFloat(11) { MinValue = 6, MaxValue = 24 }),
            UtaSkinConfiguration.TargetNoteBorder => SkinUtils.As<TValue>(new BindableFloat(2) { MinValue = 0, MaxValue = 5 }),
            UtaSkinConfiguration.LyricsCurrentColour => SkinUtils.As<TValue>(new Bindable<Color4>(new Color4(244, 247, 255, 255))),
            UtaSkinConfiguration.LyricsSungColour => SkinUtils.As<TValue>(new Bindable<Color4>(new Color4(150, 224, 255, 255))),
            UtaSkinConfiguration.LyricsReadingColour => SkinUtils.As<TValue>(new Bindable<Color4>(new Color4(212, 200, 245, 255))),
            UtaSkinConfiguration.LyricsUpcomingColour => SkinUtils.As<TValue>(new Bindable<Color4>(new Color4(185, 194, 217, 255))),
            UtaSkinConfiguration.LyricsCurrentSize => SkinUtils.As<TValue>(new BindableFloat(31) { MinValue = 22, MaxValue = 64 }),
            UtaSkinConfiguration.LyricsReadingSize => SkinUtils.As<TValue>(new BindableFloat(11.5f) { MinValue = 9, MaxValue = 24 }),
            UtaSkinConfiguration.LyricsUpcomingSize => SkinUtils.As<TValue>(new BindableFloat(18) { MinValue = 14, MaxValue = 36 }),
            _ => null,
        };
    }

    private static UtaSkinComponents componentFor(ISkinComponentLookup lookup) => lookup switch
    {
        UtaTargetNoteLookup => UtaSkinComponents.TargetNote,
        UtaCurveLookup { Role: UtaCurveRole.Reference } => UtaSkinComponents.SongPitchCurve,
        UtaCurveLookup => UtaSkinComponents.LivePitchCurve,
        UtaGridLookup => UtaSkinComponents.Grid,
        UtaLyricsDecorationLookup => UtaSkinComponents.LyricsPanel,
        UtaScoringFeedbackLookup => UtaSkinComponents.ScoringFeedback,
        _ => UtaSkinComponents.Grid,
    };

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
            // Total score is already published through the authoritative
            // ScoreProcessor bindable. Keep lazer's skinnable counter rather than
            // rendering and refreshing a second ruleset-owned score display.
            if (component is SongProgress or GameplayScoreCounter)
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
