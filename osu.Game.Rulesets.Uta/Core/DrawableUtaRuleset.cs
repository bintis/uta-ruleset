// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Recording;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Rulesets.Uta.Skinning;
using osu.Game.Rulesets.Uta.UI;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Uta.Core;

public sealed partial class DrawableUtaRuleset : DrawableRuleset<UtaHitObject>
{
    private readonly UtaAudioRouter audioRouter = new();
    private readonly UtaAudioSettingsState audioSettings = new();
    private readonly UtaPracticeController practiceController;
    private readonly UtaPitchViewport pitchViewport;
    private readonly UtaGameplayScoringController scoringController;
    private readonly UtaRecordingRuntime recordingRuntime;
    private readonly IReadOnlyList<Mod> selectedMods;
    private readonly bool scoringEnabled;
    private readonly bool recordingEnabled;

    public new UtaInputManager KeyBindingInputManager => (UtaInputManager)base.KeyBindingInputManager;

    public DrawableUtaRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods)
        : base(ruleset, prepareBeatmap(beatmap, mods), mods)
    {
        selectedMods = mods ?? [];
        scoringEnabled = selectedMods.All(mod => mod is not UtaModRelax);
        recordingEnabled = selectedMods.Any(mod => mod is UtaModRecording);

        practiceController = new UtaPracticeController((UtaBeatmap)beatmap);
        pitchViewport = new UtaPitchViewport((UtaBeatmap)beatmap);
        scoringController = new UtaGameplayScoringController((UtaBeatmap)beatmap, scoringEnabled, scoringEnabled || recordingEnabled);
        recordingRuntime = new UtaRecordingRuntime(
            (UtaBeatmap)beatmap,
            scoringController,
            recordingEnabled,
            scoringEnabled || recordingEnabled);
        Overlays.Add(scoringController);
        Overlays.Add(recordingRuntime);
        if (recordingEnabled)
            Overlays.Add(new UtaRecordingHud());
        Overlays.Add(new UtaQuickSettingsContainer());
        Overlays.Add(new UtaAudioController());
        Overlays.Add(new UtaPerformanceDiagnostics());
        Overlays.Add(new UtaGapSkipController((UtaBeatmap)beatmap));
        Overlays.Add(practiceController);
        Overlays.Add(pitchViewport);
        Overlays.Add(new UtaVolumeOverlayExtension());
        if (scoringEnabled)
            Overlays.Add(new UtaScoringHud());
    }

    private static IBeatmap prepareBeatmap(IBeatmap beatmap, IReadOnlyList<Mod>? mods)
    {
        bool scoringEnabled = mods?.Any(mod => mod is UtaModRelax) != true;
        foreach (UtaNote note in beatmap.HitObjects.OfType<UtaNote>())
            note.ScoringEnabled = scoringEnabled;

        return beatmap;
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        audioSettings.Initialise((UtaRulesetConfigManager)Config);
        audioSettings.KeyShiftSemitones.Value = selectedMods.OfType<UtaModTranspose>().SingleOrDefault()?.Semitones ?? 0;
        dependencies.CacheAs((UtaBeatmap)Beatmap);
        dependencies.CacheAs(audioRouter);
        dependencies.CacheAs(audioSettings);
        dependencies.CacheAs(practiceController);
        dependencies.CacheAs(pitchViewport);
        dependencies.CacheAs(scoringController);
        dependencies.CacheAs(recordingRuntime);
        return dependencies;
    }

    protected override Playfield CreatePlayfield() => new UtaPlayfield(Mods);

    protected override PassThroughInputManager CreateInputManager() => new UtaInputManager(Ruleset.RulesetInfo);

    public override DrawableHitObject<UtaHitObject> CreateDrawableRepresentation(UtaHitObject hitObject)
        => new DrawableUtaHitObject(hitObject);

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        audioSettings.Dispose();
        audioRouter.Dispose();
    }
}

internal sealed partial class UtaPlayfield : Playfield
{
    private readonly UtaLyricsDisplay? lyrics;

    public UtaPlayfield(IReadOnlyList<Mod> mods)
    {
        if (mods.All(mod => mod is not UtaModHidePitchGuide))
            AddInternal(new UtaPitchGuide());
        if (mods.All(mod => mod is not UtaModHideLyrics))
            AddInternal(lyrics = new UtaLyricsDisplay());
    }

    [BackgroundDependencyLoader]
    private void load(UtaBeatmap beatmap) => lyrics?.SetSegments(beatmap.Transcript);
}

internal sealed partial class DrawableUtaHitObject : DrawableHitObject<UtaHitObject>
{
    public override bool DisplayResult => false;

    private UtaGameplayScoringController scoringController = null!;

    public DrawableUtaHitObject(UtaHitObject hitObject)
        : base(hitObject)
    {
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load(UtaGameplayScoringController scoringController)
        => this.scoringController = scoringController;

    protected override JudgementResult CreateResult(Judgement judgement)
        => HitObject is UtaNote ? new UtaJudgementResult(HitObject, judgement) : base.CreateResult(judgement);

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (HitObject is not UtaNote note)
        {
            if (timeOffset >= 0)
                ApplyResult(HitResult.IgnoreHit);
            return;
        }

        if (!scoringController.ScoringEnabled)
        {
            if (timeOffset >= 0)
                ApplyResult(HitResult.IgnoreHit);
            return;
        }

        // Only the post-note-end calls matter for diagnosing whether this drawable ever gets a
        // chance to query its result - the pre-start calls (timeOffset < 0, normal every frame
        // while the note is upcoming) burned the whole log budget last round with nothing useful.
        if (timeOffset >= 0)
            scoringController.RecordPostEndCheck();

        if (timeOffset * 1000 < UtaScoringOptions.DEFAULT_COMMIT_DELAY_MICROSECONDS)
        {
            if (timeOffset >= 0 && scoringController.TryClaimDiagnosticCheckLogSlot())
            {
                Logger.Log($"Uta debug scoring check: scoringIndex={note.ScoringIndex} timeOffset={timeOffset:0.###}ms "
                           + $"(below {UtaScoringOptions.DEFAULT_COMMIT_DELAY_MICROSECONDS / 1000.0}ms commit delay) judged={Result?.HasResult}");
            }

            return;
        }

        scoringController.RecordCommitDelayPassed();

        if (!scoringController.TryGetCompletedNote(note.ScoringIndex, out UtaNoteScore? score) || score == null)
        {
            if (scoringController.TryClaimDiagnosticCheckLogSlot())
            {
                Logger.Log($"Uta debug scoring check: scoringIndex={note.ScoringIndex} timeOffset={timeOffset:0.###}ms "
                           + "past commit delay but TryGetCompletedNote failed");
            }

            return;
        }

        if (scoringController.TryClaimDiagnosticApplyLogSlot())
        {
            Logger.Log($"Uta debug scoring apply: index={note.ScoringIndex} judgementType={HitObject.Judgement.GetType().Name} "
                       + $"grade={score.Grade} nativeResult={score.NativeResult} min={HitObject.Judgement.MinResult} max={HitObject.Judgement.MaxResult}");
        }

        ApplyResult(static (result, state) => ((UtaJudgementResult)result).Populate(state.Score, state.Epoch),
            new ResultState(score, scoringController.TimelineEpoch));
        scoringController.RecordNativeApplication();
    }

    // DrawableHitObject.UpdateState() invokes this for EVERY state transition, including the
    // initial ArmedState.Idle setup that happens before the note is ever judged (not just once a
    // real Hit/Miss result lands). Expiring unconditionally killed the object's lifetime almost
    // immediately, long before the async microphone-driven judgement could ever arrive - so it was
    // never polled again and never actually judged (see debug scoring check log: postEndChecks
    // stuck at a single ~0ms sample per note, commitDelayPassed always 0). Only expire once a real
    // judgement (Hit or Miss) has been armed.
    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state != ArmedState.Idle)
            Expire();
    }

    private readonly record struct ResultState(UtaNoteScore Score, int Epoch);
}
