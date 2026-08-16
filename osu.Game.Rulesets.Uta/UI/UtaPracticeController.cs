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

/// <summary>
/// Owns practice-session state: manual A/B loop points, optional current-phrase looping,
/// and phrase navigation. Phrases are derived once from the same transcript/target-note gap
/// analysis <see cref="UtaGapSkipController"/> uses for skippable gaps. Every jump goes through
/// <see cref="GameplayClockContainer.Seek"/>, so BGM/VOX resync and pitch-history clearing
/// already happen via the existing <see cref="GameplayClockContainer.OnSeek"/> wiring.
/// </summary>
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

        // The two loop mechanisms are mutually exclusive so their repeat behaviour never fights.
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

    /// <summary>The index of the phrase most recently started at or before <paramref name="time"/>.</summary>
    internal static int PhraseIndexAt(IReadOnlyList<UtaGapSkipController.Phrase> phrases, double time)
    {
        int index = 0;
        for (int i = 1; i < phrases.Count; i++)
        {
            if (phrases[i].StartTime > time)
                break;
            index = i;
        }

        return index;
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
