// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Uta.Configuration;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI.HUD.Feedback;

/// <summary>
/// A pooled, deliberately bounded backdrop inspired by common rhythm-game stage effects:
/// slow fireflies, rising starlight and falling confetti. It is constructed only by FX mode.
/// </summary>
internal sealed partial class UtaStageEffectsLayer : CompositeDrawable
{
    private const int capacity = 40;

    private readonly Particle[] particles = new Particle[capacity];
    private readonly Bindable<UtaStageEffectStyle> style = new();
    private readonly BindableFloat intensity = new();
    private readonly BindableBool reducedMotion = new();
    private double nextEmission;
    private int nextParticle;

    public UtaStageEffectsLayer()
    {
        RelativeSizeAxes = Axes.Both;
        for (int i = 0; i < particles.Length; i++)
            AddInternal(particles[i] = new Particle());
    }

    [BackgroundDependencyLoader]
    private void load(UtaRulesetConfigManager config)
    {
        style.BindTo(config.GetBindable<UtaStageEffectStyle>(UtaRulesetSetting.StageEffectStyle));
        intensity.BindTo(config.GetBindable<float>(UtaRulesetSetting.ParticleIntensity));
        reducedMotion.BindTo(config.GetBindable<bool>(UtaRulesetSetting.ReducedMotion));
        style.BindValueChanged(_ => clear(), true);
    }

    protected override void Update()
    {
        base.Update();

        if (reducedMotion.Value || intensity.Value <= 0 || DrawWidth <= 0 || DrawHeight <= 0 || Time.Current < nextEmission)
            return;

        // At the default 65% setting this is 8 particles per second. The hard pool cap means
        // long songs cannot grow memory or draw work.
        nextEmission = Time.Current + 220 - intensity.Value * 130;
        emit();
    }

    private void emit()
    {
        Particle particle = particles[nextParticle++ % particles.Length];
        int seed = nextParticle * 1103515245 + 12345;
        float x = MathF.Abs(seed % 1000) / 1000f * DrawWidth;
        float variance = MathF.Abs((seed / 1000) % 1000) / 1000f;

        switch (style.Value)
        {
            case UtaStageEffectStyle.Starlight:
                particle.Show(new Vector2(x, DrawHeight + 8), new Vector2(2 + variance * 3), starlightColour(seed), 0.32f + intensity.Value * 0.28f, true);
                particle.MoveToY(-12, 1250 + variance * 900, Easing.OutSine);
                particle.FadeOut(1250 + variance * 900, Easing.OutSine);
                break;

            case UtaStageEffectStyle.Confetti:
                particle.Show(new Vector2(x, -14), new Vector2(4 + variance * 3, 10 + variance * 10), confettiColour(seed), 0.35f + intensity.Value * 0.3f, false);
                particle.RotateTo((seed % 90) - 45);
                particle.MoveToOffset(new Vector2((variance - 0.5f) * 150, DrawHeight + 32), 1450 + variance * 700, Easing.InQuad);
                particle.RotateTo((seed % 360) - 180, 1450 + variance * 700, Easing.OutSine);
                particle.FadeOut(1450 + variance * 700, Easing.InQuad);
                break;

            default:
                particle.Show(new Vector2(x, DrawHeight * (0.35f + variance * 0.55f)), new Vector2(4 + variance * 6), fireflyColour(seed), 0.22f + intensity.Value * 0.32f, true);
                particle.MoveToOffset(new Vector2((variance - 0.5f) * 100, -50 - variance * 90), 2400 + variance * 1200, Easing.OutSine);
                particle.FadeOut(2400 + variance * 1200, Easing.OutSine);
                break;
        }
    }

    private void clear()
    {
        foreach (Particle particle in particles)
            particle.ClearParticle();
    }

    protected override void Dispose(bool isDisposing)
    {
        style.UnbindAll();
        intensity.UnbindAll();
        reducedMotion.UnbindAll();
        base.Dispose(isDisposing);
    }

    private static Color4 fireflyColour(int seed) => (seed % 3) switch
    {
        0 => new Color4(255, 218, 108, 255),
        1 => new Color4(133, 241, 184, 255),
        _ => new Color4(126, 202, 255, 255),
    };

    private static Color4 starlightColour(int seed) => (seed % 3) switch
    {
        0 => new Color4(184, 212, 255, 255),
        1 => new Color4(255, 183, 239, 255),
        _ => new Color4(207, 175, 255, 255),
    };

    private static Color4 confettiColour(int seed) => (seed % 4) switch
    {
        0 => new Color4(255, 103, 157, 255),
        1 => new Color4(98, 208, 255, 255),
        2 => new Color4(255, 213, 95, 255),
        _ => new Color4(142, 237, 167, 255),
    };

    private sealed partial class Particle : Container
    {
        private readonly Box box;

        public Particle()
        {
            Alpha = 0;
            Child = box = new Box { RelativeSizeAxes = Axes.Both };
        }

        public void Show(Vector2 position, Vector2 size, Color4 colour, float alpha, bool round)
        {
            ClearTransforms();
            Position = position;
            Size = size;
            CornerRadius = round ? size.X / 2 : 1;
            Masking = round;
            Rotation = 0;
            box.Colour = colour;
            Alpha = alpha;
        }

        public void ClearParticle()
        {
            ClearTransforms();
            Alpha = 0;
        }
    }
}
