// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// A pitch-preserving practice speed, picked as a fixed value at song select - the same
/// icon-per-value pattern as <see cref="UtaModTranspose"/>, rather than one mod with a live
/// in-gameplay slider. BGM/VOX sync comes for free from <see cref="UI.UtaAudioController"/>,
/// which already tracks the gameplay clock's rate generically for any rate-adjusting mod.
/// </summary>
public abstract class UtaModPracticeSpeed : Mod, IApplicableToRate, IApplicableToTrack, IApplicableToSample
{
    private readonly BindableDouble speedChange;

    public double Speed => speedChange.Value;

    public override string Name => Speed == 1 ? "Original Speed" : $"Speed {Speed:0.0#}x";
    public override string Acronym => Speed == 1 ? "SPD" : $"x{Speed:0.0#}";
    public override IconUsage? Icon => FontAwesome.Solid.TachometerAlt;
    public override ModType Type => ModType.Conversion;

    public override LocalisableString Description => Speed == 1
        ? "Keep the packaged playback speed."
        : $"Play back at {Speed:0.0#}x speed without changing pitch.";

    public override Type[] IncompatibleMods => new[] { typeof(ModRateAdjust) };

    protected UtaModPracticeSpeed(int percent)
    {
        speedChange = new BindableDouble(percent / 100.0);
    }

    public double ApplyToRate(double time, double rate = 1) => rate * speedChange.Value;

    public void ApplyToTrack(IAdjustableAudioComponent track) => track.AddAdjustment(AdjustableProperty.Tempo, speedChange);

    public void ApplyToSample(IAdjustableAudioComponent sample) => sample.AddAdjustment(AdjustableProperty.Tempo, speedChange);
}

public sealed class UtaModPracticeSpeed50 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed50() : base(50) { }
}

public sealed class UtaModPracticeSpeed60 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed60() : base(60) { }
}

public sealed class UtaModPracticeSpeed70 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed70() : base(70) { }
}

public sealed class UtaModPracticeSpeed80 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed80() : base(80) { }
}

public sealed class UtaModPracticeSpeed90 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed90() : base(90) { }
}

public sealed class UtaModPracticeSpeed100 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed100() : base(100) { }
}

public sealed class UtaModPracticeSpeed110 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed110() : base(110) { }
}

public sealed class UtaModPracticeSpeed120 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed120() : base(120) { }
}

public sealed class UtaModPracticeSpeed130 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed130() : base(130) { }
}

public sealed class UtaModPracticeSpeed140 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed140() : base(140) { }
}

public sealed class UtaModPracticeSpeed150 : UtaModPracticeSpeed
{
    public UtaModPracticeSpeed150() : base(150) { }
}
