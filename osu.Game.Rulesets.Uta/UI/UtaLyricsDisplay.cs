// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Rulesets.Uta.Skinning;
using osu.Game.Rulesets.Uta.UI.HUD.Lyrics;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

internal partial class UtaLyricsRenderer : CompositeDrawable
{
    private readonly FillFlowContainer<Drawable> currentLine;
    private readonly FillFlowContainer<Drawable> nextLine;
    private readonly Container currentContainer;
    private readonly Container nextContainer;
    private readonly OsuSpriteText countdown;
    private readonly Box panel;
    private readonly Sprite panelTexture;
    private readonly Bindable<UtaLyricsPosition> lyricsPosition = new();
    private readonly Bindable<UtaLyricsSize> lyricsSize = new();
    private readonly Bindable<UtaLyricsTypeface> lyricsTypeface = new();
    private readonly BindableFloat lyricsLatency = new();
    private readonly BindableBool showUpcoming = new();
    private readonly BindableBool showReading = new();
    private readonly Bindable<UtaLyricsProgressStyle> progressStyle = new();

    private readonly UtaLyricsPresentationState presentation = new();
    private UtaWordToken[] currentTokens = Array.Empty<UtaWordToken>();
    private UtaVisualStyle style = UtaVisualStyle.Prism();
    private UtaVisualStyleProvider? styleProvider;

    public UtaLyricsRenderer()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Width = 1;
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;

        InternalChildren = new Drawable[]
        {
            panel = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = style.Lyrics.Panel,
                Alpha = style.Lyrics.PanelOpacity,
            },
            panelTexture = new Sprite
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Stretch,
                Alpha = 0,
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    currentContainer = createLineContainer(true, out currentLine),
                    nextContainer = createLineContainer(false, out nextLine),
                },
            },
        };

        currentContainer.Add(countdown = new OsuSpriteText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreRight,
            X = -12,
            Font = OsuFont.Default.With(size: 18, weight: FontWeight.Bold),
            Colour = new Color4(207, 191, 255, 255),
            Shadow = true,
            ShadowColour = Color4.Black,
        });
    }

    [BackgroundDependencyLoader]
    private void load(UtaRulesetConfigManager config, UtaAudioSettingsState audioSettings, UtaVisualStyleProvider styleProvider)
    {
        this.styleProvider = styleProvider;
        styleProvider.StyleChanged += applyStyle;
        applyStyle(styleProvider.Style);
        lyricsPosition.BindTo(config.GetBindable<UtaLyricsPosition>(UtaRulesetSetting.LyricsPosition));
        lyricsSize.BindTo(config.GetBindable<UtaLyricsSize>(UtaRulesetSetting.LyricsSize));
        lyricsTypeface.BindTo(config.GetBindable<UtaLyricsTypeface>(UtaRulesetSetting.LyricsTypeface));
        lyricsLatency.BindTo(audioSettings.LyricsLatency);
        showUpcoming.BindTo(config.GetBindable<bool>(UtaRulesetSetting.LyricsShowUpcoming));
        showReading.BindTo(config.GetBindable<bool>(UtaRulesetSetting.LyricsShowReading));
        progressStyle.BindTo(config.GetBindable<UtaLyricsProgressStyle>(UtaRulesetSetting.LyricsProgressStyle));

        lyricsPosition.BindValueChanged(_ => updateLayout(), true);
        lyricsSize.BindValueChanged(_ => updateTypography(), true);
        lyricsTypeface.BindValueChanged(_ => updateTypography(), true);
        showUpcoming.BindValueChanged(_ => rebuild(presentation.SegmentIndex), true);
        showReading.BindValueChanged(_ => rebuild(presentation.SegmentIndex), true);
        progressStyle.BindValueChanged(_ => rebuild(presentation.SegmentIndex), true);
    }

    private void applyStyle(UtaVisualStyle value)
    {
        style = value;
        panel.Colour = value.Lyrics.Panel;
        panel.Alpha = value.Lyrics.PanelOpacity;
        panelTexture.Texture = value.Assets.LyricsPanel;
        panelTexture.Alpha = value.Assets.LyricsPanel == null ? 0 : value.Lyrics.PanelOpacity;
        countdown.Colour = value.Lyrics.Countdown;
        updateTypography();
    }

    private void updateLayout()
    {
        Width = 1;
        Anchor = Origin = Anchor.Centre;
        Y = 0;
    }

    private void updateTypography()
    {
        updateLayout();
        countdown.Font = createFont(style.Lyrics.UpcomingSize * sizeMultiplier, FontWeight.Bold);
        rebuild(presentation.SegmentIndex);
    }

    public void SetSegments(IReadOnlyList<UtaTranscriptSegment> value)
    {
        presentation.SetSegments(value);
        rebuild(-1);
    }

    protected override void Update()
    {
        base.Update();

        if (presentation.Segments.Count == 0)
        {
            currentContainer.Hide();
            nextContainer.Hide();
            return;
        }

        UtaLyricsPresentationUpdate update = presentation.Update(Time.Current, lyricsLatency.Value);
        UtaLyricsFrame frame = update.Frame;
        if (update.StructuralChange)
            rebuild(frame.SegmentIndex);

        currentContainer.Alpha = frame.Visible ? 1 : 0;
        nextContainer.Alpha = showUpcoming.Value && frame.Visible && frame.SegmentIndex + 1 < presentation.Segments.Count ? 1 : 0;

        // Avoid formatting a new countdown string every rendered frame. The visible value
        // only changes at integer-second boundaries.
        if (update.CountdownChanged)
            countdown.Text = frame.Countdown?.ToString() ?? string.Empty;

        for (int i = 0; i < currentTokens.Length && i < frame.WordProgress.Count; i++)
            currentTokens[i].SetProgress(frame.WordProgress[i]);
    }

    private void rebuild(int index)
    {
        currentLine.Clear();
        nextLine.Clear();
        currentTokens = Array.Empty<UtaWordToken>();

        if (index < 0 || index >= presentation.Segments.Count)
            return;

        var current = presentation.Segments[index];
        currentTokens = createTokens(current, false, sizeMultiplier, typeface).ToArray();
        currentLine.AddRange(currentTokens);

        if (showUpcoming.Value && index + 1 < presentation.Segments.Count)
            nextLine.AddRange(createTokens(presentation.Segments[index + 1], true, sizeMultiplier, typeface));
    }

    private IEnumerable<UtaWordToken> createTokens(UtaTranscriptSegment segment, bool next, float sizeMultiplier, Typeface typeface)
    {
        bool hasReading = showReading.Value && segment.Words.Any(word => !string.IsNullOrWhiteSpace(word.Reading));
        bool addSpacing = segment.Text.Contains(' ');

        for (int i = 0; i < segment.Words.Count; i++)
        {
            yield return new UtaWordToken(segment.Words[i], hasReading, next, sizeMultiplier, typeface, style, progressStyle.Value)
            {
                Margin = new MarginPadding { Right = addSpacing && i < segment.Words.Count - 1 ? next ? 6 : 10 : 0 },
            };
        }
    }

    private static Container createLineContainer(bool current, out FillFlowContainer<Drawable> line)
    {
        line = new FillFlowContainer<Drawable>
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Full,
            Padding = new MarginPadding { Horizontal = 26, Vertical = current ? 12 : 8 },
        };

        return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Child = line,
        };
    }

    private partial class UtaWordToken : CompositeDrawable
    {
        private readonly bool next;
        private readonly bool estimated;
        private readonly OsuSpriteText readingText;
        private readonly OsuSpriteText wordText;
        private readonly Box progress;
        private readonly Sprite progressTexture;
        private readonly UtaLyricsStyle style;
        private readonly UtaLyricsProgressStyle progressStyle;

        public UtaWordToken(UtaTranscriptWord word, bool showReading, bool next, float sizeMultiplier, Typeface typeface,
                            UtaVisualStyle visualStyle, UtaLyricsProgressStyle progressStyle)
        {
            this.next = next;
            estimated = word.Estimated;
            style = visualStyle.Lyrics;
            this.progressStyle = progressStyle;
            AutoSizeAxes = Axes.Both;

            Color4 colour = word.Estimated ? style.Estimated : next ? style.Upcoming : style.Current;
            var content = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Children = new Drawable[]
                {
                    readingText = new OsuSpriteText
                    {
                        Text = showReading ? word.Reading ?? " " : string.Empty,
                        Font = OsuFont.GetFont(typeface, style.ReadingSize * sizeMultiplier, FontWeight.SemiBold),
                        Colour = next ? style.Upcoming : style.Reading,
                        Alpha = showReading ? 0.75f : 0,
                        Shadow = true,
                        ShadowColour = Color4.Black,
                    },
                    wordText = new OsuSpriteText
                    {
                        Text = word.Word,
                        Font = OsuFont.GetFont(typeface, (next ? style.UpcomingSize : style.CurrentSize) * sizeMultiplier, next ? FontWeight.Medium : FontWeight.SemiBold),
                        Colour = colour,
                        Shadow = true,
                        ShadowColour = Color4.Black,
                    },
                },
            };

            InternalChildren = new Drawable[]
            {
                content,
                new Sprite
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreRight,
                    X = -4,
                    Size = new Vector2(next ? 7 : 6),
                    Texture = next ? visualStyle.Assets.LyricsUpcomingMarker
                        : showReading ? visualStyle.Assets.LyricsReadingMarker : null,
                    Alpha = next || showReading ? 0.8f : 0,
                },
                progress = new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = style.ProgressThickness,
                    Colour = style.Sung,
                    Alpha = 0,
                },
                progressTexture = new Sprite
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = style.ProgressThickness,
                    Texture = progressStyle switch
                    {
                        UtaLyricsProgressStyle.Fill => visualStyle.Assets.LyricsProgress,
                        UtaLyricsProgressStyle.Marker => visualStyle.Assets.LyricsReadingMarker,
                        _ => visualStyle.Assets.LyricsUnderline,
                    },
                    FillMode = FillMode.Stretch,
                    Alpha = 0,
                },
            };
            Alpha = next ? word.Estimated ? 0.32f : 0.56f : word.Estimated ? 0.76f : 0.88f;
        }

        public void SetProgress(double progress)
        {
            if (next)
                return;

            float amount = (float)Math.Clamp(progress, 0, 1);
            Alpha = 0.88f + amount * 0.12f;
            switch (progressStyle)
            {
                case UtaLyricsProgressStyle.Fill:
                    this.progress.RelativeSizeAxes = Axes.Both;
                    this.progress.Width = amount;
                    this.progress.Height = 1;
                    this.progress.Alpha = amount * 0.18f;
                    progressTexture.RelativeSizeAxes = Axes.Both;
                    progressTexture.Width = amount;
                    progressTexture.Height = 1;
                    progressTexture.Alpha = progressTexture.Texture == null ? 0 : amount * 0.72f;
                    break;

                case UtaLyricsProgressStyle.Marker:
                    this.progress.RelativeSizeAxes = Axes.None;
                    this.progress.RelativePositionAxes = Axes.X;
                    this.progress.X = amount;
                    this.progress.Width = 3;
                    this.progress.Height = style.CurrentSize;
                    this.progress.Alpha = amount;
                    progressTexture.RelativeSizeAxes = Axes.None;
                    progressTexture.RelativePositionAxes = Axes.X;
                    progressTexture.X = amount;
                    progressTexture.Width = 8;
                    progressTexture.Height = style.CurrentSize;
                    progressTexture.Alpha = progressTexture.Texture == null ? 0 : amount;
                    break;

                default:
                    this.progress.Width = amount;
                    this.progress.Alpha = progressTexture.Texture == null ? amount : 0;
                    progressTexture.RelativeSizeAxes = Axes.X;
                    progressTexture.RelativePositionAxes = Axes.None;
                    progressTexture.Width = amount;
                    progressTexture.Height = style.ProgressThickness;
                    progressTexture.Alpha = progressTexture.Texture == null ? 0 : amount;
                    break;
            }

            Color4 unsung = estimated ? style.Estimated : style.Current;
            Color4 sung = estimated ? style.Estimated : style.Sung;
            Color4 colour = blend(unsung, sung, amount);
            readingText.Colour = colour;
            wordText.Colour = colour;
        }

        private static Color4 blend(Color4 from, Color4 to, float amount) => new(
            from.R + (to.R - from.R) * amount,
            from.G + (to.G - from.G) * amount,
            from.B + (to.B - from.B) * amount,
            1);
    }

    private float sizeMultiplier => lyricsSize.Value switch
    {
        UtaLyricsSize.Compact => 0.78f,
        UtaLyricsSize.Large => 1.28f,
        _ => 1,
    };

    private Typeface typeface => lyricsTypeface.Value switch
    {
        UtaLyricsTypeface.TorusAlternate => Typeface.TorusAlternate,
        UtaLyricsTypeface.Inter => Typeface.Inter,
        _ => Typeface.Torus,
    };

    private FontUsage createFont(float size, FontWeight weight) => OsuFont.GetFont(typeface, size, weight);

    protected override void Dispose(bool isDisposing)
    {
        lyricsPosition.UnbindAll();
        lyricsSize.UnbindAll();
        lyricsTypeface.UnbindAll();
        lyricsLatency.UnbindAll();
        showUpcoming.UnbindAll();
        showReading.UnbindAll();
        progressStyle.UnbindAll();
        if (styleProvider != null)
            styleProvider.StyleChanged -= applyStyle;
        base.Dispose(isDisposing);
    }
}
