// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// The single source of truth for the pitch guide's vertical viewport centre, shared by
/// <see cref="UtaPitchGuide"/>, <see cref="UtaPitchCurveGraph"/> and <see cref="UtaPitchGuideTrail"/>
/// so all three always agree on where a given pitch sits on screen.
///
/// This restores the adaptive-viewport behaviour the standalone player originally had: the
/// visible range glides to follow whichever notes are coming up next, rather than staying fixed
/// to one range for the whole song. A later revision replaced this with a single value computed
/// once from the whole song (still available as <see cref="UtaPitchGuide.CalculateFixedCentre"/>,
/// reused here as the per-window target), which is why the range stopped moving.
/// </summary>
internal sealed partial class UtaPitchViewport : osu.Framework.Graphics.Containers.CompositeDrawable
{
    // How much of the look-ahead window counts as "coming up next" for viewport purposes -
    // matches the standalone player's original relevance window.
    private const double relevance_lookahead_fraction = 0.72;
    private const float move_rate = 2.4f;

    public readonly BindableFloat CentreMidi;

    private readonly UtaNote[] notes;
    private GameplayClockContainer? gameplayClock;
    private double lastUpdateTime = double.NegativeInfinity;

    public UtaPitchViewport(UtaBeatmap beatmap)
    {
        notes = beatmap.HitObjects.OfType<UtaNote>()
                       .Where(note => note.Midi != null)
                       .OrderBy(note => note.StartTime)
                       .ToArray();
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

    // Falling back to the neutral default (rather than the whole song's range) during a long gap
    // with nothing relevant nearby matches the original standalone-player behaviour being restored.
    private float targetCentre(double current)
        => UtaPitchGuide.CalculateFixedCentre(
            notes.Where(note => note.EndTime >= current - 200
                                 && note.StartTime <= current + UtaPitchGuide.LOOK_AHEAD * relevance_lookahead_fraction)
                 .ToArray());

    /// <summary>Glides <paramref name="current"/> toward <paramref name="target"/>, capped at <see cref="move_rate"/> semitones/second.</summary>
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
