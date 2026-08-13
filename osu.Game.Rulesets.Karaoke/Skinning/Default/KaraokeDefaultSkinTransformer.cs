// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Karaoke.UI.HUD;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Karaoke.Skinning.Default;

public class KaraokeDefaultSkinTransformer : SkinTransformer
{
    private readonly KaraokeSkin karaokeSkin;

    public KaraokeDefaultSkinTransformer(ISkin skin, IBeatmap beatmap)
        : base(skin)
    {
        karaokeSkin = new KaraokeSkin(new SkinInfo(), new InternalSkinStorageResourceProvider("Default"));
    }

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        => karaokeSkin.GetConfig<TLookup, TValue>(lookup);

    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        // SkinnableContainer asks for a persisted user layout before the normal
        // ruleset lookup. Those layouts may contain the entire historical karaoke
        // HUD, so deliberately fall through to the clean Uta layout below.
        if (lookup.GetType().Name == "UserSkinComponentLookup"
            && lookup.GetType().GetField("Component")?.GetValue(lookup) is GlobalSkinnableContainerLookup persistedLookup
            && persistedLookup.Lookup == GlobalSkinnableContainers.MainHUDComponents
            && persistedLookup.Ruleset != null)
            return null;

        switch (lookup)
        {
            case GlobalSkinnableContainerLookup containerLookup:
                // Only handle ruleset level defaults for now.
                if (containerLookup.Ruleset == null)
                    return base.GetDrawableComponent(lookup);

                switch (containerLookup.Lookup)
                {
                    case GlobalSkinnableContainers.MainHUDComponents:
                        // The old karaoke HUD inherited a complete skin HUD (leaderboard,
                        // health, combo and duplicate timing meters). The Uta fork only
                        // needs the shortcut router which opens lazer's native quick settings;
                        // lazer's core pause/progress interface remains outside this layer.
                        return new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                new SettingButtonsDisplay
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    UsesFixedAnchor = true,
                                },
                            },
                        };

                    default:
                        return base.GetDrawableComponent(lookup);
                }

            default:
                return base.GetDrawableComponent(lookup);
        }
    }

}
