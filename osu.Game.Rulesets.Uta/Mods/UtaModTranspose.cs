// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

public abstract class UtaModTranspose : Mod, IApplicableMod
{
    private static readonly Type[] transpose_mod_types =
    {
        typeof(UtaModTransposeMinus6),
        typeof(UtaModTransposeMinus5),
        typeof(UtaModTransposeMinus4),
        typeof(UtaModTransposeMinus3),
        typeof(UtaModTransposeMinus2),
        typeof(UtaModTransposeMinus1),
        typeof(UtaModTransposeOriginal),
        typeof(UtaModTransposePlus1),
        typeof(UtaModTransposePlus2),
        typeof(UtaModTransposePlus3),
        typeof(UtaModTransposePlus4),
        typeof(UtaModTransposePlus5),
        typeof(UtaModTransposePlus6),
    };

    internal static IReadOnlyList<Type> AllTransposeModTypes => transpose_mod_types;

    public int Semitones { get; }

    public override string Name => Semitones switch
    {
        < 0 => $"Transpose {Semitones}",
        > 0 => $"Transpose +{Semitones}",
        _ => "Original key",
    };

    public override string Acronym => Semitones switch
    {
        < 0 => $"K{Semitones}",
        > 0 => $"K+{Semitones}",
        _ => "KEY",
    };

    public override LocalisableString Description => Semitones == 0
        ? "Keep the packaged song key."
        : $"Transpose the accompaniment, vocals, target notes and scoring pitch by {Semitones:+0;-0} semitones.";
    public override IconUsage? Icon => FontAwesome.Solid.Music;
    public override ModType Type => ModType.Conversion;

    /// <summary>
    /// A key can only have one transpose value. Declaring the concrete sibling
    /// types makes lazer disable them in the MOD selector as soon as one is on.
    /// </summary>
    public override Type[] IncompatibleMods => transpose_mod_types.Where(type => type != GetType())
                                                                       .Append(typeof(UtaModDaycore))
                                                                       .Append(typeof(UtaModNightcore))
                                                                       .ToArray();

    protected UtaModTranspose(int semitones)
    {
        Semitones = semitones;
    }

    public static Mod? Create(int semitones) => semitones switch
    {
        -6 => new UtaModTransposeMinus6(),
        -5 => new UtaModTransposeMinus5(),
        -4 => new UtaModTransposeMinus4(),
        -3 => new UtaModTransposeMinus3(),
        -2 => new UtaModTransposeMinus2(),
        -1 => new UtaModTransposeMinus1(),
        1 => new UtaModTransposePlus1(),
        2 => new UtaModTransposePlus2(),
        3 => new UtaModTransposePlus3(),
        4 => new UtaModTransposePlus4(),
        5 => new UtaModTransposePlus5(),
        6 => new UtaModTransposePlus6(),
        _ => null,
    };
}

public sealed class UtaModTransposeMinus6 : UtaModTranspose
{
    public UtaModTransposeMinus6() : base(-6) { }
}

public sealed class UtaModTransposeMinus5 : UtaModTranspose
{
    public UtaModTransposeMinus5() : base(-5) { }
}

public sealed class UtaModTransposeMinus4 : UtaModTranspose
{
    public UtaModTransposeMinus4() : base(-4) { }
}

public sealed class UtaModTransposeMinus3 : UtaModTranspose
{
    public UtaModTransposeMinus3() : base(-3) { }
}

public sealed class UtaModTransposeMinus2 : UtaModTranspose
{
    public UtaModTransposeMinus2() : base(-2) { }
}

public sealed class UtaModTransposeMinus1 : UtaModTranspose
{
    public UtaModTransposeMinus1() : base(-1) { }
}

public sealed class UtaModTransposeOriginal : UtaModTranspose
{
    public UtaModTransposeOriginal() : base(0) { }
}

public sealed class UtaModTransposePlus1 : UtaModTranspose
{
    public UtaModTransposePlus1() : base(1) { }
}

public sealed class UtaModTransposePlus2 : UtaModTranspose
{
    public UtaModTransposePlus2() : base(2) { }
}

public sealed class UtaModTransposePlus3 : UtaModTranspose
{
    public UtaModTransposePlus3() : base(3) { }
}

public sealed class UtaModTransposePlus4 : UtaModTranspose
{
    public UtaModTransposePlus4() : base(4) { }
}

public sealed class UtaModTransposePlus5 : UtaModTranspose
{
    public UtaModTransposePlus5() : base(5) { }
}

public sealed class UtaModTransposePlus6 : UtaModTranspose
{
    public UtaModTransposePlus6() : base(6) { }
}
