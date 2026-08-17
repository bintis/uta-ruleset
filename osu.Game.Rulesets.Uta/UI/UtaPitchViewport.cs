// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.UI;

internal sealed partial class UtaPitchViewport : osu.Framework.Graphics.Containers.CompositeDrawable
{
    private const double relevance_lookahead_fraction = 0.72;
    private const float move_rate = 2.4f;
    private const float neutral_centre = 57.5f;
    private const float edge_margin = 1.5f;

    public readonly BindableFloat CentreMidi;

    private readonly UtaNote[] notes;
    private readonly double maximumNoteDuration;
    private GameplayClockContainer? gameplayClock;
    private double lastUpdateTime = double.NegativeInfinity;

    public UtaPitchViewport(UtaBeatmap beatmap)
    {
        notes = beatmap.HitObjects.OfType<UtaNote>()
                       .Where(note => note.Midi != null)
                       .OrderBy(note => note.StartTime)
                       .ToArray();
        maximumNoteDuration = notes.Length == 0 ? 0 : notes.Max(note => note.Duration);
        CentreMidi = new BindableFloat(UtaPitchGuide.CalculateFixedCentre(notes));
    }

    [BackgroundDependencyLoader]
    private void load(GameplayClockContainer gameplayClock)
    {
        this.gameplayClock = gameplayClock;
        gameplayClock.OnSeek += onSeek;
    }

    protected override void Update()
    {
        base.Update();

        if (notes.Length == 0)
            return;

        double current = Time.Current;
        float dt = double.IsFinite(lastUpdateTime) && Math.Abs(current - lastUpdateTime) <= 550
            ? Math.Clamp((float)((current - lastUpdateTime) / 1000), 0, 0.05f)
            : 0;
        lastUpdateTime = current;

        CentreMidi.Value = StepCentre(CentreMidi.Value, targetCentre(current), dt);
    }

    private void onSeek() => CentreMidi.Value = targetCentre(gameplayClock!.CurrentTime);

    // Hot path: no LINQ, closure or temporary array on rendered frames.
    private float targetCentre(double current)
    {
        double visibleStart = current - 200;
        double visibleEnd = current + UtaPitchGuide.LOOK_AHEAD * relevance_lookahead_fraction;
        int end = upperBoundStart(visibleEnd);
        int start = lowerBoundStart(visibleStart - maximumNoteDuration);
        float low = float.PositiveInfinity;
        float high = float.NegativeInfinity;
        bool found = false;

        for (int i = start; i < end; i++)
        {
            UtaNote note = notes[i];
            if (note.EndTime < visibleStart)
                continue;

            float midi = (float)note.Midi!.Value;
            low = Math.Min(low, midi);
            high = Math.Max(high, midi);
            found = true;
        }

        return found ? calculateCentre(low, high) : neutral_centre;
    }

    private int lowerBoundStart(double time)
    {
        int low = 0;
        int high = notes.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (notes[middle].StartTime < time)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private int upperBoundStart(double time)
    {
        int low = 0;
        int high = notes.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (notes[middle].StartTime <= time)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private static float calculateCentre(float low, float high)
    {
        float targetCentre = neutral_centre;
        if (high - low > UtaPitchGuide.VIEW_SPAN - edge_margin * 2)
            targetCentre = (low + high) / 2;
        else if (low < 48)
            targetCentre = low - edge_margin + UtaPitchGuide.VIEW_SPAN / 2;
        else if (high > 67)
            targetCentre = high + edge_margin - UtaPitchGuide.VIEW_SPAN / 2;

        return MathF.Round(Math.Clamp(targetCentre, 40 + UtaPitchGuide.VIEW_SPAN / 2, 88 - UtaPitchGuide.VIEW_SPAN / 2) * 2) / 2;
    }

    internal static float StepCentre(float current, float target, float dt)
    {
        if (Math.Abs(target - current) < 0.2f)
            return current;

        float alpha = 1 - MathF.Exp(-dt / 0.85f);
        float desired = (target - current) * alpha;
        return current + Math.Clamp(desired, -move_rate * dt, move_rate * dt);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (gameplayClock != null)
            gameplayClock.OnSeek -= onSeek;
        base.Dispose(isDisposing);
    }
}
