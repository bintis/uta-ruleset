// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Opts the current play into microphone recording. Recording begins when
/// gameplay starts and the completed take is saved with the performance archive.
/// </summary>
public sealed class UtaModRecording : Mod, IApplicableMod
{
    public override string Name => "Recording";

    public override string Acronym => "REC";

    public override LocalisableString Description
        => "Record and save the post-input-gain microphone take for this performance.";

    public override IconUsage? Icon => FontAwesome.Solid.Microphone;

    public override ModType Type => ModType.Fun;

    public override Type[] IncompatibleMods => new[] { typeof(UtaModAutoplay) };
}
