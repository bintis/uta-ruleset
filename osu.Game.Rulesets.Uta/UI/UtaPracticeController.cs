// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.UI;

internal sealed partial class UtaPracticeController : CompositeDrawable, IKeyBindingHandler<UtaAction>
{
    private const double phrase_restart_threshold = 1500;

    public readonly Bindable<double?> LoopPointA = new();
    public readonly Bindable<double?> LoopPointB = new();
    public readonly BindableBool LoopCurrentPhrase = new();

    public IReadOnlyList<UtaGapSkipController.Phrase> Phrases { get; }

    private readonly BindableFloat phraseLoopLeadIn = new();
    private readonly BindableBool debugDiagnostics = new();
    private GameplayClockContainer gameplayClock = null!;
    private DrawableRuleset drawableRuleset = null!;

    public UtaPracticeController(UtaBeatmap beatmap)
    {
        RelativeSizeAxes = Axes.Both;
        Phrases = UtaGapSkipController.FindPhrases(beatmap.Transcript, beatmap.HitObjects.OfType<UtaNote>());
    }

    [BackgroundDependencyLoader]
    private void load(GameplayClockContainer clock, DrawableRuleset drawableRuleset, UtaAudioSettingsState audioSettings)
    {
        gameplayClock = clock;
        this.drawableRuleset = drawableRuleset;
        phraseLoopLeadIn.BindTo(audioSettings.PhraseLoopLeadIn);
        debugDiagnostics.BindTo(audioSettings.DebugDiagnostics);

        LoopPointA.BindValueChanged(_ => LoopCurrentPhrase.Value = false);
        LoopPointB.BindValueChanged(_ => LoopCurrentPhrase.Value = false);
        LoopCurrentPhrase.BindValueChanged(value =>
        {
            if (value.NewValue)
                ClearLoopPoints();
        });
    }

    protected override void Update()
    {
        base.Update();

        if (Phrases.Count == 0)
            return;

        double current = gameplayClock.CurrentTime;

        if (LoopPointA.Value is { } a && LoopPointB.Value is { } b && b > a && current >= b)
        {
            seek(a, "A-B loop repeat");
            return;
        }

        if (LoopCurrentPhrase.Value)
        {
            UtaGapSkipController.Phrase phrase = Phrases[phraseIndexAt(current)];
            if (current >= phrase.EndTime)
                seek(System.Math.Max(0, phrase.StartTime - phraseLoopLeadIn.Value), "phrase loop repeat");
        }
    }

    public void SetLoopPointA() => LoopPointA.Value = gameplayClock.CurrentTime;

    public void SetLoopPointB() => LoopPointB.Value = gameplayClock.CurrentTime;

    public void ClearLoopPoints()
    {
        LoopPointA.Value = null;
        LoopPointB.Value = null;
    }

    public void GoToPreviousPhrase()
    {
        if (Phrases.Count == 0)
            return;

        double current = gameplayClock.CurrentTime;
        int index = phraseIndexAt(current);

        if (current - Phrases[index].StartTime > phrase_restart_threshold)
        {
            seek(Phrases[index].StartTime, "previous phrase (restart)");
            return;
        }

        int previous = index - 1;
        seek(previous >= 0 ? Phrases[previous].StartTime : 0, "previous phrase");
    }

    public void GoToNextPhrase()
    {
        if (Phrases.Count == 0)
            return;

        int next = phraseIndexAt(gameplayClock.CurrentTime) + 1;
        if (next < Phrases.Count)
            seek(Phrases[next].StartTime, "next phrase");
    }

    public void RetryPhrase()
    {
        if (Phrases.Count == 0)
            return;

        seek(Phrases[phraseIndexAt(gameplayClock.CurrentTime)].StartTime, "retry phrase");
    }

    private int phraseIndexAt(double time) => PhraseIndexAt(Phrases, time);

    internal static int PhraseIndexAt(IReadOnlyList<UtaGapSkipController.Phrase> phrases, double time)
    {
        // Phrases are ordered by start time. The previous linear scan ran every rendered
        // frame while phrase looping was enabled; upper-bound lookup keeps that path O(log n).
        int low = 0;
        int high = phrases.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (phrases[middle].StartTime <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return System.Math.Max(0, low - 1);
    }

    private void seek(double target, string context)
        => UtaGameplaySeeker.Seek(gameplayClock, drawableRuleset, action => Schedule(action), target, $"practice {context}", debugDiagnostics.Value);

    public bool OnPressed(KeyBindingPressEvent<UtaAction> e)
    {
        switch (e.Action)
        {
            case UtaAction.SetLoopPointA:
                SetLoopPointA();
                return true;

            case UtaAction.SetLoopPointB:
                SetLoopPointB();
                return true;

            case UtaAction.ClearLoopPoints:
                ClearLoopPoints();
                return true;

            case UtaAction.PreviousPhrase:
                GoToPreviousPhrase();
                return true;

            case UtaAction.NextPhrase:
                GoToNextPhrase();
                return true;

            case UtaAction.RetryPhrase:
                RetryPhrase();
                return true;

            case UtaAction.ToggleCurrentPhraseLoop:
                LoopCurrentPhrase.Value = !LoopCurrentPhrase.Value;
                return true;

            default:
                return false;
        }
    }

    public void OnReleased(KeyBindingReleaseEvent<UtaAction> e)
    {
    }

    protected override void Dispose(bool isDisposing)
    {
        phraseLoopLeadIn.UnbindAll();
        debugDiagnostics.UnbindAll();
        base.Dispose(isDisposing);
    }
}
