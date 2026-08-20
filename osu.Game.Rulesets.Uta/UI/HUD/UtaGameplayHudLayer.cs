// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Recording;
using osu.Game.Rulesets.Uta.Skinning;
using osu.Game.Rulesets.Uta.UI.HUD.Lyrics;
using osu.Game.Rulesets.Uta.UI.HUD.Pitch;
using osu.Game.Rulesets.Uta.UI.HUD.Feedback;
using osuTK;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Uta.UI.HUD;

/// <summary>
/// The single ruleset-owned parent for all uta! gameplay HUD presentation.
/// Gameplay time, microphone processing and scoring remain owned by their existing services.
/// </summary>
internal sealed partial class UtaGameplayHudLayer : CompositeDrawable
{
    private readonly UtaPitchHudHost? pitchHost;
    private readonly UtaLyricsHudHost? lyricsHost;
    private readonly UtaSingingParticleLayer? singingParticles;
    private readonly UtaScoringFeedbackLayer? scoringFeedback;
    private readonly UtaScoringHud? scoreHud;
    private readonly Bindable<UtaLyricsPosition> lyricsPosition = new();
    private readonly BindableBool reducedMotion = new();
    private readonly Bindable<UtaPitchHudSize> pitchHudSize = new();
    private readonly BindableFloat pitchHudOpacity = new();
    private readonly Bindable<UtaPitchHudLayout> pitchHudLayout = new();
    private readonly BindableFloat lyricsPanelOpacity = new();
    private readonly BindableFloat safeAreaPadding = new();
    private readonly UtaVisualStyleProvider styleProvider = new();
    private readonly bool showPitch;
    private readonly bool showLyrics;
    private readonly bool showScore;
    private readonly bool showPractice;
    private readonly bool showRecording;
    private Vector2 lastSize = new(float.NaN);
    private UtaLyricsPosition lastLyricsPosition;
    private UtaHudDensity lastDensity = (UtaHudDensity)(-1);
    private ISkinSource? skinSource;

    internal bool HasSingingParticles => singingParticles != null;
    internal bool HasScoringFeedback => scoringFeedback != null;

    public UtaGameplayHudLayer(bool showPitch, bool showLyrics, bool showScore, bool showPractice, bool showRecording)
    {
        this.showPitch = showPitch;
        this.showLyrics = showLyrics;
        this.showScore = showScore;
        this.showPractice = showPractice;
        this.showRecording = showRecording;
        RelativeSizeAxes = Axes.Both;

        if (showPitch)
            AddInternal(pitchHost = new UtaPitchHudHost());
        if (showLyrics)
            AddInternal(lyricsHost = new UtaLyricsHudHost());
        if (showPitch)
            AddInternal(singingParticles = new UtaSingingParticleLayer());
        if (showScore)
        {
            AddInternal(scoringFeedback = new UtaScoringFeedbackLayer());
            AddInternal(scoreHud = new UtaScoringHud());
        }
        if (showPractice)
            AddInternal(new UtaPracticeHud());
        if (showRecording)
            AddInternal(new UtaRecordingHud());
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        dependencies.CacheAs(styleProvider);
        return dependencies;
    }

    [BackgroundDependencyLoader]
    private void load(UtaRulesetConfigManager config, ISkinSource skinSource)
    {
        this.skinSource = skinSource;
        skinSource.SourceChanged += refreshStyle;
        lyricsPosition.BindTo(config.GetBindable<UtaLyricsPosition>(UtaRulesetSetting.LyricsPosition));
        reducedMotion.BindTo(config.GetBindable<bool>(UtaRulesetSetting.ReducedMotion));
        pitchHudSize.BindTo(config.GetBindable<UtaPitchHudSize>(UtaRulesetSetting.PitchHudSize));
        pitchHudOpacity.BindTo(config.GetBindable<float>(UtaRulesetSetting.PitchHudOpacity));
        pitchHudLayout.BindTo(config.GetBindable<UtaPitchHudLayout>(UtaRulesetSetting.PitchHudLayout));
        lyricsPanelOpacity.BindTo(config.GetBindable<float>(UtaRulesetSetting.LyricsPanelOpacity));
        safeAreaPadding.BindTo(config.GetBindable<float>(UtaRulesetSetting.HudSafeAreaPadding));
        lyricsPosition.BindValueChanged(_ => invalidateLayout(), true);
        reducedMotion.BindValueChanged(_ => refreshStyle(), true);
        pitchHudSize.BindValueChanged(_ => invalidateLayout(), true);
        pitchHudLayout.BindValueChanged(_ => invalidateLayout(), true);
        safeAreaPadding.BindValueChanged(_ => invalidateLayout(), true);
        pitchHudOpacity.BindValueChanged(_ => refreshStyle(), true);
        lyricsPanelOpacity.BindValueChanged(_ => refreshStyle(), true);
    }

    protected override void Update()
    {
        base.Update();

        if (DrawSize == lastSize && lyricsPosition.Value == lastLyricsPosition)
            return;

        lastSize = DrawSize;
        lastLyricsPosition = lyricsPosition.Value;
        UtaHudLayoutSnapshot layout = UtaHudLayoutCoordinator.Calculate(
            DrawWidth,
            DrawHeight,
            lyricsPosition.Value,
            showPitch,
            showLyrics,
            showScore,
            showPractice,
            showRecording,
            safeAreaPadding.Value);
        RectangleF pitchBounds = layout.PitchBounds;
        if (showPitch)
        {
            float scale = pitchHudSize.Value switch
            {
                UtaPitchHudSize.Compact => 0.9f,
                UtaPitchHudSize.Large => 1.15f,
                _ => 1,
            };
            pitchBounds = new RectangleF(
                pitchBounds.X,
                pitchBounds.Y,
                pitchBounds.Width,
                Math.Clamp(pitchBounds.Height * scale, UtaHudLayoutCoordinator.MINIMUM_PITCH_HEIGHT, 260));
            if (pitchHudLayout.Value == UtaPitchHudLayout.FullWidth)
                pitchBounds = new RectangleF(0, pitchBounds.Y, DrawWidth, pitchBounds.Height);
        }
        layout = layout with { PitchBounds = pitchBounds };
        if (layout.Density != lastDensity)
        {
            lastDensity = layout.Density;
            refreshStyle();
        }
        pitchHost?.ApplyLayout(layout.PitchBounds);
        lyricsHost?.ApplyLayout(layout.LyricsBounds);
        singingParticles?.ApplyLayout(layout.PitchBounds);
        scoreHud?.ApplyLayout(layout.ScoreBounds);
        scoringFeedback?.ApplyLayout(layout.ScoreBounds);
    }

    private void invalidateLayout() => lastSize = new Vector2(float.NaN);

    private void refreshStyle()
    {
        if (skinSource == null)
            return;

        UtaHudDensity density = lastDensity < 0 ? UtaHudDensity.Standard : lastDensity;
        styleProvider.Set(UtaSkinStyleResolver.Resolve(
            skinSource,
            density,
            reducedMotion.Value,
            pitchHudOpacity.Value,
            lyricsPanelOpacity.Value));
    }

    protected override void Dispose(bool isDisposing)
    {
        lyricsPosition.UnbindAll();
        reducedMotion.UnbindAll();
        pitchHudSize.UnbindAll();
        pitchHudOpacity.UnbindAll();
        pitchHudLayout.UnbindAll();
        lyricsPanelOpacity.UnbindAll();
        safeAreaPadding.UnbindAll();
        if (skinSource != null)
            skinSource.SourceChanged -= refreshStyle;
        base.Dispose(isDisposing);
    }
}
