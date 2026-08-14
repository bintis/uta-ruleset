// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.UI;

namespace osu.Game.Rulesets.Uta.Core;

public sealed partial class DrawableUtaRuleset : DrawableRuleset<UtaHitObject>
{
    public new UtaInputManager KeyBindingInputManager => (UtaInputManager)base.KeyBindingInputManager;

    public DrawableUtaRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods)
        : base(ruleset, beatmap, mods)
    {
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        dependencies.CacheAs((UtaBeatmap)Beatmap);
        return dependencies;
    }

    protected override Playfield CreatePlayfield() => new UtaPlayfield();

    protected override PassThroughInputManager CreateInputManager() => new UtaInputManager(Ruleset.RulesetInfo);

    public override DrawableHitObject<UtaHitObject> CreateDrawableRepresentation(UtaHitObject hitObject)
        => new DrawableUtaHitObject(hitObject);
}

internal sealed partial class UtaPlayfield : Playfield
{
    private readonly UtaLyricsDisplay lyrics;

    public UtaPlayfield()
    {
        AddInternal(new UtaPitchGuide());
        AddInternal(lyrics = new UtaLyricsDisplay());
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap) => lyrics.SetSegments(beatmap.Transcript);
}

internal sealed partial class DrawableUtaHitObject : DrawableHitObject<UtaHitObject>
{
    public override bool DisplayResult => false;

    public DrawableUtaHitObject(UtaHitObject hitObject)
        : base(hitObject)
    {
        Alpha = 0;
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (timeOffset >= 0)
            ApplyMaxResult();
    }

    protected override void UpdateHitStateTransforms(ArmedState state) => Expire();
}
