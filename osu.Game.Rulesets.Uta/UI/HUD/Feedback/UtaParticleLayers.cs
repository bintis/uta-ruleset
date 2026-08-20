// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Rulesets.Uta.Skinning;
using osu.Game.Rulesets.UI;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI.HUD.Feedback;

internal abstract partial class UtaBoundedParticleLayer : CompositeDrawable
{
    private readonly Particle[] particles;
    private int nextParticle;

    protected UtaVisualStyle Style { get; private set; } = UtaVisualStyle.Prism();

    protected UtaBoundedParticleLayer(int capacity)
    {
        RelativeSizeAxes = Axes.Both;
        particles = new Particle[capacity];
        for (int i = 0; i < particles.Length; i++)
            AddInternal(particles[i] = new Particle());
    }

    protected void SetStyle(UtaVisualStyle style)
    {
        Style = style;
        Texture? texture = ParticleTexture(style);
        foreach (Particle particle in particles)
            particle.Texture = texture;
    }

    protected abstract Texture? ParticleTexture(UtaVisualStyle style);

    protected void Emit(Vector2 position, Color4 colour, float intensity, int configuredLimit)
    {
        int limit = Math.Clamp(configuredLimit, 0, particles.Length);
        if (limit == 0 || intensity <= 0)
            return;

        if (nextParticle >= limit)
            nextParticle = 0;
        Particle particle = particles[nextParticle++];
        particle.ClearTransforms();
        particle.Position = position;
        particle.Colour = colour;
        particle.Scale = new Vector2(0.65f + intensity * 0.5f);
        particle.Alpha = Math.Clamp(0.35f + intensity * 0.5f, 0, 0.9f);
        particle.MoveToOffset(new Vector2((nextParticle % 5 - 2) * 5, -20 - nextParticle % 4 * 3), 520, Easing.OutQuint);
        particle.FadeOut(520, Easing.OutQuint);
    }

    private sealed partial class Particle : CircularContainer
    {
        private readonly UtaTexturedPrimitive primitive;

        public Texture? Texture
        {
            set => primitive.Texture = value;
        }

        public Particle()
        {
            Size = new Vector2(7);
            Masking = true;
            Alpha = 0;
            Child = primitive = new UtaTexturedPrimitive { RelativeSizeAxes = Axes.Both };
        }
    }
}

internal sealed partial class UtaSingingParticleLayer : UtaBoundedParticleLayer
{
    private readonly BindableBool voiceActive = new();
    private readonly BindableFloat intensity = new();
    private RectangleF pitchBounds;
    private double lastEmissionTime = double.NegativeInfinity;
    private UtaVisualStyleProvider? styleProvider;

    public UtaSingingParticleLayer()
        : base(18)
    {
    }

    protected override Texture? ParticleTexture(UtaVisualStyle style) => style.Assets.ParticleSing;

    [BackgroundDependencyLoader]
    private void load(DrawableRuleset drawableRuleset, UtaRulesetConfigManager config, UtaVisualStyleProvider styleProvider)
    {
        this.styleProvider = styleProvider;
        styleProvider.StyleChanged += SetStyle;
        SetStyle(styleProvider.Style);
        if (drawableRuleset is DrawableUtaRuleset utaRuleset)
            voiceActive.BindTo(utaRuleset.KeyBindingInputManager.LiveVoiceActive);
        intensity.BindTo(config.GetBindable<float>(UtaRulesetSetting.ParticleIntensity));
    }

    public void ApplyLayout(RectangleF pitchBounds) => this.pitchBounds = pitchBounds;

    protected override void Update()
    {
        base.Update();
        if (!voiceActive.Value || Style.Motion.ReducedMotion || Time.Current - lastEmissionTime < 90)
            return;

        lastEmissionTime = Time.Current;
        float yFraction = 0.25f + (float)(Time.Current % 700) / 1400;
        Emit(
            new Vector2(pitchBounds.X + pitchBounds.Width * 0.25f, pitchBounds.Y + pitchBounds.Height * yFraction),
            Style.Pitch.LiveAccurate,
            intensity.Value,
            Style.Motion.MaxSingingParticles);
    }

    protected override void Dispose(bool isDisposing)
    {
        voiceActive.UnbindAll();
        intensity.UnbindAll();
        if (styleProvider != null)
            styleProvider.StyleChanged -= SetStyle;
        base.Dispose(isDisposing);
    }
}

internal sealed partial class UtaScoringFeedbackLayer : UtaBoundedParticleLayer
{
    private readonly BindableFloat intensity = new();
    private UtaGameplayScoringController? scoringController;
    private UtaVisualStyleProvider? styleProvider;
    private RectangleF scoreBounds;

    public UtaScoringFeedbackLayer()
        : base(24)
    {
    }

    protected override Texture? ParticleTexture(UtaVisualStyle style) => style.Assets.ParticleScore;

    [BackgroundDependencyLoader]
    private void load(UtaGameplayScoringController scoringController, UtaRulesetConfigManager config, UtaVisualStyleProvider styleProvider)
    {
        this.scoringController = scoringController;
        this.styleProvider = styleProvider;
        styleProvider.StyleChanged += SetStyle;
        SetStyle(styleProvider.Style);
        intensity.BindTo(config.GetBindable<float>(UtaRulesetSetting.ParticleIntensity));
        scoringController.NoteCompleted += onNoteCompleted;
    }

    public void ApplyLayout(RectangleF scoreBounds) => this.scoreBounds = scoreBounds;

    private void onNoteCompleted(UtaNoteScore score)
    {
        if (Style.Motion.ReducedMotion || score.Grade is not (UtaNoteGrade.Perfect or UtaNoteGrade.Great))
            return;

        Color4 colour = score.Grade == UtaNoteGrade.Perfect ? Style.Feedback.Perfect : Style.Feedback.Great;
        Vector2 origin = scoreBounds == RectangleF.Empty
            ? new Vector2(DrawWidth / 2, 72)
            : new Vector2(scoreBounds.Centre.X, scoreBounds.Bottom);
        Emit(origin, colour, intensity.Value, Style.Motion.MaxScoringParticles);
    }

    protected override void Dispose(bool isDisposing)
    {
        intensity.UnbindAll();
        if (scoringController != null)
            scoringController.NoteCompleted -= onNoteCompleted;
        if (styleProvider != null)
            styleProvider.StyleChanged -= SetStyle;
        base.Dispose(isDisposing);
    }
}
