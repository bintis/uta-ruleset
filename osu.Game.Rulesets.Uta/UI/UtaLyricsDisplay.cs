// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Formats;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

public partial class UtaLyricsDisplay : CompositeDrawable
{
    private readonly FillFlowContainer<Drawable> currentLine;
    private readonly FillFlowContainer<Drawable> nextLine;
    private readonly Container currentContainer;
    private readonly Container nextContainer;
    private readonly OsuSpriteText countdown;
    private readonly Bindable<UtaLyricsPosition> lyricsPosition = new();
    private readonly Bindable<UtaLyricsSize> lyricsSize = new();
    private readonly Bindable<UtaLyricsTypeface> lyricsTypeface = new();
    private readonly BindableFloat lyricsLatency = new();

    private IReadOnlyList<UtaTranscriptSegment> segments = Array.Empty<UtaTranscriptSegment>();
    private UtaWordToken[] currentTokens = Array.Empty<UtaWordToken>();
    private double[] wordProgress = Array.Empty<double>();
    private int segmentIndex = -1;

    public UtaLyricsDisplay()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Width = 0.80f;
        Anchor = Anchor.BottomCentre;
        Origin = Anchor.BottomCentre;
        Y = -42;

        InternalChild = new FillFlowContainer
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
    private void load(UtaRulesetConfigManager config, UtaAudioSettingsState audioSettings)
    {
        lyricsPosition.BindTo(config.GetBindable<UtaLyricsPosition>(UtaRulesetSetting.LyricsPosition));
        lyricsSize.BindTo(config.GetBindable<UtaLyricsSize>(UtaRulesetSetting.LyricsSize));
        lyricsTypeface.BindTo(config.GetBindable<UtaLyricsTypeface>(UtaRulesetSetting.LyricsTypeface));
        lyricsLatency.BindTo(audioSettings.LyricsLatency);

        lyricsPosition.BindValueChanged(_ => updateLayout(), true);
        lyricsSize.BindValueChanged(_ => updateTypography(), true);
        lyricsTypeface.BindValueChanged(_ => updateTypography(), true);
    }

    private void updateLayout()
    {
        Width = lyricsSize.Value switch
        {
            UtaLyricsSize.Compact => 0.72f,
            UtaLyricsSize.Large => 0.90f,
            _ => 0.80f,
        };

        switch (lyricsPosition.Value)
        {
            case UtaLyricsPosition.Top:
                Anchor = Origin = Anchor.TopCentre;
                Y = 210;
                break;

            case UtaLyricsPosition.Centre:
                Anchor = Origin = Anchor.Centre;
                Y = 36;
                break;

            default:
                Anchor = Origin = Anchor.BottomCentre;
                Y = -42;
                break;
        }
    }

    private void updateTypography()
    {
        updateLayout();
        countdown.Font = createFont(18 * sizeMultiplier, FontWeight.Bold);
        rebuild(segmentIndex);
    }

    public void SetSegments(IReadOnlyList<UtaTranscriptSegment> value)
    {
        segments = UtaLyricsTimeline.Normalize(value);
        segmentIndex = -1;
        rebuild(-1);
    }

    protected override void Update()
    {
        base.Update();

        if (segments.Count == 0)
        {
            currentContainer.Hide();
            nextContainer.Hide();
            return;
        }

        double seconds = (Time.Current - lyricsLatency.Value) / 1000;
        var frame = UtaLyricsTimeline.Evaluate(segments, seconds, Math.Max(0, segmentIndex), wordProgress);
        if (frame.SegmentIndex != segmentIndex)
            rebuild(frame.SegmentIndex);

        currentContainer.Alpha = frame.Visible ? 1 : 0;
        nextContainer.Alpha = frame.Visible && frame.SegmentIndex + 1 < segments.Count ? 1 : 0;
        countdown.Text = frame.Countdown?.ToString() ?? string.Empty;

        for (int i = 0; i < currentTokens.Length && i < frame.WordProgress.Count; i++)
            currentTokens[i].SetProgress(frame.WordProgress[i]);

    }

    private void rebuild(int index)
    {
        segmentIndex = index;
        currentLine.Clear();
        nextLine.Clear();
        currentTokens = Array.Empty<UtaWordToken>();
        wordProgress = Array.Empty<double>();

        if (index < 0 || index >= segments.Count)
            return;

        var current = segments[index];
        currentTokens = createTokens(current, false, sizeMultiplier, typeface).ToArray();
        wordProgress = new double[currentTokens.Length];
        currentLine.AddRange(currentTokens);

        if (index + 1 < segments.Count)
            nextLine.AddRange(createTokens(segments[index + 1], true, sizeMultiplier, typeface));
    }

    private static IEnumerable<UtaWordToken> createTokens(UtaTranscriptSegment segment, bool next, float sizeMultiplier, Typeface typeface)
    {
        bool showReading = segment.Words.Any(word => !string.IsNullOrWhiteSpace(word.Reading));
        bool addSpacing = segment.Text.Contains(' ');

        for (int i = 0; i < segment.Words.Count; i++)
        {
            yield return new UtaWordToken(segment.Words[i], showReading, next, sizeMultiplier, typeface)
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

        public UtaWordToken(UtaTranscriptWord word, bool showReading, bool next, float sizeMultiplier, Typeface typeface)
        {
            this.next = next;
            estimated = word.Estimated;
            AutoSizeAxes = Axes.Both;

            Color4 colour = word.Estimated ? new Color4(220, 207, 238, 255) : new Color4(229, 231, 242, 255);
            var content = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Children = new Drawable[]
                {
                    readingText = new OsuSpriteText
                    {
                        Text = showReading ? word.Reading ?? " " : string.Empty,
                        Font = OsuFont.GetFont(typeface, (next ? 9.5f : 11.5f) * sizeMultiplier, FontWeight.SemiBold),
                        Colour = colour,
                        Alpha = showReading ? 0.75f : 0,
                        Shadow = true,
                        ShadowColour = Color4.Black,
                    },
                    wordText = new OsuSpriteText
                    {
                        Text = word.Word,
                        Font = OsuFont.GetFont(typeface, (next ? 18 : 31) * sizeMultiplier, next ? FontWeight.Medium : FontWeight.SemiBold),
                        Colour = colour,
                        Shadow = true,
                        ShadowColour = Color4.Black,
                    },
                },
            };

            InternalChild = content;
            Alpha = next ? word.Estimated ? 0.32f : 0.56f : word.Estimated ? 0.76f : 0.88f;
        }

        public void SetProgress(double progress)
        {
            if (next)
                return;

            float amount = (float)Math.Clamp(progress, 0, 1);
            Alpha = 0.88f + amount * 0.12f;

            Color4 unsung = estimated ? new Color4(220, 207, 238, 255) : new Color4(229, 231, 242, 255);
            Color4 sung = estimated ? new Color4(201, 188, 255, 255) : new Color4(150, 224, 255, 255);
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
        base.Dispose(isDisposing);
    }
}
