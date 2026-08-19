// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Game.Rulesets.Uta.Core;

namespace osu.Game.Rulesets.Uta.UI.HUD.Lyrics;

internal sealed partial class UtaLyricsHudHost : CompositeDrawable
{
    private readonly UtaLyricsRenderer renderer;

    public UtaLyricsHudHost()
    {
        InternalChild = renderer = new UtaLyricsRenderer();
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap) => renderer.SetSegments(beatmap.Transcript);

    public void ApplyLayout(RectangleF bounds)
    {
        Position = bounds.Location;
        Size = bounds.Size;
    }
}
