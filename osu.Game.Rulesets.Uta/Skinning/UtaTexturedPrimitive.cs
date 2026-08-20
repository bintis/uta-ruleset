// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;

namespace osu.Game.Rulesets.Uta.Skinning;

/// <summary>
/// A skin texture layered over an always-present ruleset fallback. Keeping the
/// fallback visible makes missing and fully-transparent skin assets safe for
/// gameplay-critical cues.
/// </summary>
internal partial class UtaTexturedPrimitive : CompositeDrawable
{
    private readonly Sprite texture;

    public Texture? Texture
    {
        get => texture.Texture;
        set
        {
            texture.Texture = value;
            texture.Alpha = value == null ? 0 : 1;
        }
    }

    public UtaTexturedPrimitive()
    {
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both },
            texture = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Stretch,
                Alpha = 0,
            },
        };
    }
}
