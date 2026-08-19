// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;

namespace osu.Game.Rulesets.Uta.UI.HUD.Pitch;

internal sealed partial class UtaPitchHudHost : CompositeDrawable
{
    public UtaPitchHudHost()
    {
        Masking = true;
        InternalChild = new UtaPitchGuideRenderer();
    }

    public void ApplyLayout(RectangleF bounds)
    {
        Position = bounds.Location;
        Size = bounds.Size;
    }
}
