// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input;
using osu.Framework.Logging;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Input.Handlers;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Recording;
using osu.Game.Rulesets.Uta.Gameplay;
using osu.Game.Rulesets.Uta.Global;
using osu.Game.Rulesets.Uta.Remote;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Rulesets.Uta.Skinning;
using osu.Game.Rulesets.Uta.UI;
using osu.Game.Rulesets.Uta.UI.HUD;
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
    private readonly UtaQuickSettingsContainer quickSettings;
    private readonly UtaRuntimeModeState runtimeModes;
    private readonly UtaGameplaySessionBridge gameplaySessionBridge;
    private readonly UtaGlobalExtension gameplayServices;
    private readonly IReadOnlyList<Mod> selectedMods;
    private IBindable<IReadOnlyList<Mod>> liveMods = null!;
    private IBindable<IReadOnlyList<Mod>>? gameWideMods;
    private readonly bool scoringEnabled;
    private readonly bool recordingEnabled;
    private readonly bool practiceEnabled;

    public new UtaInputManager KeyBindingInputManager => (UtaInputManager)base.KeyBindingInputManager;

    internal UtaRuntimeModeState RuntimeModes => runtimeModes;

    public DrawableUtaRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods)
        : this(ruleset, beatmap, ReconcileConstructorMods(
            mods,
            UtaRulesetRuntime.Instance.SelectedCoreRate,
            UtaRulesetRuntime.Instance.AuthoritativeModSelectionKnown,
            UtaRulesetRuntime.Instance.SelectedTranspose), true)
    {
    }

    private DrawableUtaRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> reconciledMods, bool _)
        : base(ruleset, prepareBeatmap(beatmap, reconciledMods), reconciledMods)
    {
        selectedMods = reconciledMods;
        scoringEnabled = selectedMods.All(mod => mod is not UtaModRelax);
        recordingEnabled = selectedMods.Any(mod => mod is UtaModRecording);
        practiceEnabled = selectedMods.Any(mod => mod is UtaModPractice);
        runtimeModes = new UtaRuntimeModeState();
        runtimeModes.OriginalVocalsEnabled.Value = UtaRulesetRuntime.Instance.ShouldPlayOriginalVocals(
            selectedMods.Any(mod => mod is UtaModOriginalVocals));
        runtimeModes.OctaveFoldEnabled.Value = ((UtaBeatmap)beatmap).OctaveTolerance
                                                || selectedMods.Any(mod => mod is UtaModOctaveFold);
        Logger.Log(
            $"Uta debug ruleset mods: [{string.Join(",", selectedMods.Select(mod => mod.Acronym))}] "
            + $"originalVocalsEnabled={runtimeModes.OriginalVocalsEnabled.Value} "
            + $"preferred={UtaRulesetRuntime.Instance.OriginalVocalsPreferred} "
            + $"octaveFoldEnabled={runtimeModes.OctaveFoldEnabled.Value}");

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
        gameplayServices = new UtaGlobalExtension();
        bool immersiveQueueEnabled = selectedMods.Any(mod => mod is UtaModImmersiveQueue);
        bool stageEffectsEnabled = selectedMods.Any(mod => mod is UtaModStageEffects);
        gameplaySessionBridge = new UtaGameplaySessionBridge(
            (UtaBeatmap)beatmap,
            immersiveQueueEnabled);
        Overlays.Add(gameplaySessionBridge);
        Overlays.Add(new UtaGameplayHudLayer(
            true,
            true,
            scoringEnabled,
            practiceEnabled,
            recordingEnabled,
            stageEffectsEnabled));
        quickSettings = new UtaQuickSettingsContainer(stageEffectsEnabled);
        Overlays.Add(quickSettings);
        Overlays.Add(new UtaAudioController());
        Overlays.Add(new UtaPerformanceDiagnostics());
        Overlays.Add(new UtaGapSkipController((UtaBeatmap)beatmap));
        Overlays.Add(practiceController);
        Overlays.Add(pitchViewport);
        Overlays.Add(new UtaVolumeOverlayExtension());
        Overlays.Add(gameplayServices);
        // PlayerLoader can construct this drawable with an empty leased mod list and
        // attach the authoritative SongSelect selection a frame later. The prompt itself
        // waits for the playback coordinator to confirm IQ, so keep it available here.
        Overlays.Add(new UtaImmersiveRemotePrompt(gameplayServices.RemoteServerController, gameplayServices.Playback));
    }

    internal static IReadOnlyList<Mod> ReconcileConstructorMods(
        IReadOnlyList<Mod>? mods,
        double? authoritativeRate,
        bool authoritativeTransposeKnown,
        int? authoritativeTranspose)
    {
        IReadOnlyList<Mod> source = mods ?? [];
        bool needsRateReconcile = authoritativeRate.HasValue;
        bool needsTransposeReconcile = authoritativeTransposeKnown;

        if (!needsRateReconcile && !needsTransposeReconcile)
            return source;

        var reconciled = source.Where(mod => (!needsRateReconcile || mod is not UtaModNightcore and not UtaModDaycore)
                                             && (!needsTransposeReconcile || mod is not UtaModTranspose)).ToList();

        if (needsRateReconcile)
        {
            if (Math.Abs(authoritativeRate!.Value - 1.5) < 0.000001)
                reconciled.Add(new UtaModNightcore());
            else if (Math.Abs(authoritativeRate.Value - 0.75) < 0.000001)
                reconciled.Add(new UtaModDaycore());
        }

        if (needsTransposeReconcile && authoritativeTranspose.HasValue && UtaModTranspose.Create(authoritativeTranspose.Value) is Mod transpose)
            reconciled.Add(transpose);

        return reconciled;
    }

    internal static IReadOnlyList<Mod> ReconcileCoreRateMods(IReadOnlyList<Mod>? mods, double? authoritativeRate)
        => ReconcileConstructorMods(mods, authoritativeRate, false, null);

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
        bool selectedOriginalVocals = selectedMods.Any(mod => mod is UtaModOriginalVocals);

        runtimeModes.OriginalVocalsEnabled.Value = ResolveInitialOriginalVocals(
            selectedOriginalVocals,
            UtaRulesetRuntime.Instance.OriginalVocalsPreferred);
        dependencies.CacheAs((UtaBeatmap)Beatmap);
        dependencies.CacheAs(KeyBindingInputManager);
        dependencies.CacheAs(audioRouter);
        dependencies.CacheAs(audioSettings);
        dependencies.CacheAs(practiceController);
        dependencies.CacheAs(pitchViewport);
        dependencies.CacheAs(scoringController);
        dependencies.CacheAs(recordingRuntime);
        dependencies.CacheAs(runtimeModes);
        dependencies.CacheAs(gameplaySessionBridge);
        dependencies.CacheAs(gameplayServices.GameplaySessions);
        dependencies.CacheAs(gameplayServices.Playback);
        dependencies.CacheAs(quickSettings.Overlay);
        return dependencies;
    }

    [Resolved(canBeNull: true)]
    private OsuGameBase? game { get; set; }

    [BackgroundDependencyLoader]
    private void load(IBindable<IReadOnlyList<Mod>> mods)
    {
        liveMods = mods.GetBoundCopy();
        if (game != null)
        {
            object? selected = typeof(OsuGameBase)
                .GetField("SelectedMods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(game);
            if (selected is IBindable<IReadOnlyList<Mod>> gameMods)
                gameWideMods = gameMods.GetBoundCopy();
        }

        liveMods.BindValueChanged(onLiveModsChanged, true);
        gameWideMods?.BindValueChanged(_ => applyRuntimeModsFromMods(), true);
        audioSettings.OriginalVocalsEnabled.BindValueChanged(onOriginalVocalsSettingChanged);
    }

    private void onLiveModsChanged(ValueChangedEvent<IReadOnlyList<Mod>> change)
    {
        // Only a falling edge on the live bindable is an explicit VOX off.
        // constructor=[] after 切歌 must not clear the persisted preference.
        if (hasOriginalVocals(change.OldValue) && !hasOriginalVocals(change.NewValue))
        {
            UtaRulesetRuntime.Instance.RememberOriginalVocals(false);
            audioSettings.OriginalVocalsEnabled.Value = false;
        }

        applyRuntimeModsFromMods();
    }

    private void applyRuntimeModsFromMods()
    {
        applyOriginalVocalsFromMods();

        int transpose = ResolveRuntimeTranspose(
            UtaRulesetRuntime.Instance.AuthoritativeModSelectionKnown,
            UtaRulesetRuntime.Instance.SelectedTranspose,
            gameWideMods?.Value,
            liveMods.Value,
            selectedMods);
        if (MathF.Round(audioSettings.KeyShiftSemitones.Value) != transpose)
        {
            Logger.Log(
                $"Uta transpose synced to {transpose:+0;-0;0}st "
                + $"authoritative={UtaRulesetRuntime.Instance.AuthoritativeModSelectionKnown} "
                + $"remembered={UtaRulesetRuntime.Instance.SelectedTranspose?.ToString() ?? "none"} "
                + $"constructor=[{formatMods(selectedMods)}] "
                + $"live=[{formatMods(liveMods.Value)}] "
                + $"game=[{formatMods(gameWideMods?.Value)}]");
            audioSettings.KeyShiftSemitones.Value = transpose;
        }

        double selectedRate = ResolveCoreRate(selectedMods);
        double desiredRate = UtaRulesetRuntime.Instance.SelectedCoreRate
                             ?? ResolveCoreRate(gameWideMods?.Value, liveMods.Value, selectedMods);
        double correction = desiredRate / selectedRate;
        if (Math.Abs(audioSettings.RuntimeModFrequency.Value - correction) > 0.000001)
        {
            Logger.Log(
                $"Uta core rate synced to {desiredRate:0.00}x (correction={correction:0.00}x) "
                + $"constructor=[{formatMods(selectedMods)}] "
                + $"live=[{formatMods(liveMods.Value)}] "
                + $"game=[{formatMods(gameWideMods?.Value)}]");
            audioSettings.RuntimeModFrequency.Value = correction;
        }
    }

    internal static int ResolveInitialTranspose(IReadOnlyList<Mod> selectedMods, int? rememberedTranspose)
        => selectedMods.OfType<UtaModTranspose>().SingleOrDefault()?.Semitones ?? rememberedTranspose ?? 0;

    internal static int? ResolveTranspose(params IReadOnlyList<Mod>?[] modLists)
    {
        foreach (IReadOnlyList<Mod>? mods in modLists)
        {
            UtaModTranspose? transpose = mods?.OfType<UtaModTranspose>().FirstOrDefault();
            if (transpose != null)
                return transpose.Semitones;
        }

        return null;
    }

    internal static int? ResolveTransposeOrRemembered(int? remembered, params IReadOnlyList<Mod>?[] modLists)
        => ResolveTranspose(modLists) ?? remembered;

    internal static int ResolveRuntimeTranspose(bool authoritativeKnown, int? authoritativeTranspose, params IReadOnlyList<Mod>?[] modLists)
        => authoritativeKnown ? authoritativeTranspose ?? 0 : ResolveTranspose(modLists) ?? authoritativeTranspose ?? 0;

    internal static double ResolveCoreRate(params IReadOnlyList<Mod>?[] modLists)
    {
        foreach (IReadOnlyList<Mod>? mods in modLists)
        {
            if (mods?.Any(mod => mod is UtaModNightcore) == true)
                return 1.5;
            if (mods?.Any(mod => mod is UtaModDaycore) == true)
                return 0.75;
        }

        return 1;
    }

    private void onOriginalVocalsSettingChanged(ValueChangedEvent<bool> change)
    {
        UtaRulesetRuntime.Instance.RememberOriginalVocals(change.NewValue);
        applyOriginalVocalsFromMods();
    }

    private void applyOriginalVocalsFromMods()
    {
        bool fromMods = hasOriginalVocals(selectedMods)
                        || hasOriginalVocals(liveMods.Value)
                        || hasOriginalVocals(gameWideMods?.Value);
        bool enabled = UtaRulesetRuntime.Instance.ShouldPlayOriginalVocals(fromMods);
        if (runtimeModes.OriginalVocalsEnabled.Value == enabled)
            return;

        Logger.Log(
            $"Uta original vocals {(enabled ? "on" : "off")} "
            + $"constructor=[{formatMods(selectedMods)}] "
            + $"live=[{formatMods(liveMods.Value)}] "
            + $"game=[{formatMods(gameWideMods?.Value)}] "
            + $"preferred={UtaRulesetRuntime.Instance.OriginalVocalsPreferred}");
        runtimeModes.OriginalVocalsEnabled.Value = enabled;
    }

    internal static bool ResolveInitialOriginalVocals(bool selectedOriginalVocals, bool rememberedOriginalVocals)
        => selectedOriginalVocals || rememberedOriginalVocals;

    internal static bool ShouldClearPersistedOriginalVocals(bool selectedOriginalVocals, bool rememberedOriginalVocals)
        => !selectedOriginalVocals && !rememberedOriginalVocals;

    private static bool hasOriginalVocals(IReadOnlyList<Mod>? mods)
        => mods?.Any(mod => mod is UtaModOriginalVocals) == true;

    private static string formatMods(IReadOnlyList<Mod>? mods)
        => mods == null ? string.Empty : string.Join(",", mods.Select(mod => mod.Acronym));

    protected override void LoadComplete()
    {
        base.LoadComplete();
        UtaGameplaySeeker.DisableFrameStability(this);

        audioSettings.KeyShiftSemitones.Value = ResolveInitialTranspose(
            selectedMods,
            UtaRulesetRuntime.Instance.SelectedTranspose);

        bool selectedOriginalVocals = selectedMods.Any(mod => mod is UtaModOriginalVocals);
        if (ShouldClearPersistedOriginalVocals(selectedOriginalVocals, UtaRulesetRuntime.Instance.OriginalVocalsPreferred))
            audioSettings.OriginalVocalsEnabled.Value = false;
    }

    protected override Playfield CreatePlayfield() => new UtaPlayfield();

    protected override PassThroughInputManager CreateInputManager() => new UtaInputManager(Ruleset.RulesetInfo);

    public override DrawableHitObject<UtaHitObject> CreateDrawableRepresentation(UtaHitObject hitObject)
        => new DrawableUtaHitObject(hitObject);

    protected override void Dispose(bool isDisposing)
    {
        liveMods?.UnbindAll();
        gameWideMods?.UnbindAll();
        base.Dispose(isDisposing);
        audioSettings.Dispose();
        audioRouter.Dispose();
    }
}

internal sealed partial class UtaPlayfield : Playfield
{
}

internal sealed partial class DrawableUtaHitObject : DrawableHitObject<UtaHitObject>
{
    public override bool DisplayResult => false;

    public void CompleteAsMiss()
    {
        if (!Result.HasResult)
            ApplyMinResult();
    }

    private const double result_retry_interval_milliseconds = 10;

    private UtaGameplayScoringController scoringController = null!;
    private double nextResultPollOffset = double.NegativeInfinity;

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
        if (scoringController.ForceCompletionRequested)
            return;

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

        // CheckForResult runs on every render update. A streaming score cannot exist
        // before its fixed commit delay, so query exactly at that boundary rather than
        // repeatedly taking the scoring-session lock while a note is upcoming. A missing
        // result afterwards is retried at 100 Hz, ahead of the 10 ms microphone cadence.
        if (timeOffset < nextResultPollOffset)
            return;

        double commitDelayMilliseconds = UtaScoringOptions.DEFAULT_COMMIT_DELAY_MICROSECONDS / 1000.0;
        if (timeOffset < commitDelayMilliseconds)
        {
            nextResultPollOffset = commitDelayMilliseconds;
            return;
        }

        scoringController.RecordPostEndCheck();
        scoringController.RecordCommitDelayPassed();

        if (!scoringController.TryGetCompletedNote(note.ScoringIndex, out UtaNoteScore? score) || score == null)
        {
            nextResultPollOffset = timeOffset + result_retry_interval_milliseconds;
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
