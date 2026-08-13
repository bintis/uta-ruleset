// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Caching;
using osu.Game.Graphics;
using osu.Game.Rulesets.Karaoke.UI.Position;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Karaoke.UI.Components;

public partial class RealTimeScoringVisualization : VoiceVisualization<KeyValuePair<double, KaraokeScoringAction>>
{
    private readonly Cached addStateCache = new();

    protected override float PathRadius => 2.5f;

    protected override float Offset => DrawSize.X;

    [Resolved]
    private INotePositionInfo notePositionInfo { get; set; } = null!;

    public RealTimeScoringVisualization()
    {
        Masking = true;
    }

    protected override double GetTime(KeyValuePair<double, KaraokeScoringAction> frame) => frame.Key;

    protected override float GetPosition(KeyValuePair<double, KaraokeScoringAction> frame) => notePositionInfo.Calculator.YPositionAt(frame.Value);

    private bool createNew = true;
    private int currentColourBand = -1;

    private double minAvailableTime;

    public void AddAction(KaraokeScoringAction action)
    {
        if (Time.Current <= minAvailableTime)
            return;

        minAvailableTime = Time.Current;

        int colourBand = Math.Clamp((int)Math.Round(action.Similarity * 12), 0, 12);
        if (colourBand != currentColourBand)
        {
            createNew = true;
            currentColourBand = colourBand;
        }

        if (createNew)
        {
            createNew = false;

            CreateNew(new KeyValuePair<double, KaraokeScoringAction>(Time.Current, action));
            Paths.Last().Colour = performanceColour(action.Similarity);
        }
        else
        {
            Append(new KeyValuePair<double, KaraokeScoringAction>(Time.Current, action));
        }

        // Trigger update last frame
        addStateCache.Invalidate();
    }

    public void Release()
    {
        if (Time.Current < minAvailableTime)
            return;

        minAvailableTime = Time.Current;

        createNew = true;
        currentColourBand = -1;
    }

    protected override void Update()
    {
        // If addStateCache is invalid, means last path should be re-calculate
        if (!addStateCache.IsValid && Paths.Any())
        {
            var updatePath = Paths.Last();
            MarkAsInvalid(updatePath);
            addStateCache.Validate();
        }

        base.Update();
    }

    [BackgroundDependencyLoader]
    private void load(OsuColour colours)
    {
        Colour = colours.Yellow;
    }

    private static Color4 performanceColour(float quality)
    {
        ReadOnlySpan<(float At, Color4 Colour)> stops =
        [
            (0f, new Color4(244, 63, 94, 255)),
            (0.3f, new Color4(251, 113, 133, 255)),
            (0.7f, new Color4(250, 176, 24, 255)),
            (0.94f, new Color4(250, 204, 21, 255)),
            (1f, new Color4(255, 245, 157, 255)),
        ];

        quality = Math.Clamp(quality, 0, 1);
        for (int i = 1; i < stops.Length; i++)
        {
            if (quality > stops[i].At)
                continue;

            var left = stops[i - 1];
            var right = stops[i];
            float amount = (quality - left.At) / (right.At - left.At);
            return new Color4(
                interpolate(left.Colour.R, right.Colour.R, amount),
                interpolate(left.Colour.G, right.Colour.G, amount),
                interpolate(left.Colour.B, right.Colour.B, amount),
                1f);
        }

        return stops[^1].Colour;

        static float interpolate(float left, float right, float amount)
            => left + (right - left) * amount;
    }
}
