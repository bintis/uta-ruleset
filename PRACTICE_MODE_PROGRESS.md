# Practice mode implementation progress

## Related fix: pitch guide viewport stopped auto-adjusting

Separate from the checklist below: the pitch indicator's visible note range (e.g. "C3-C4") used
to glide to follow whichever notes were coming up next, but had been replaced at some point by a
single range computed once for the whole song and never updated again - so it stopped moving.

Root cause: an earlier revision (`2da615a`, well before this practice-mode work) replaced a
per-frame adaptive-viewport algorithm with a one-time `UtaPitchGuide.CalculateFixedCentre(notes)`
call. That static method is still correct and still used (now called every frame against a
rolling window of nearby notes instead of once against the whole song).

Fix: new `UI/UtaPitchViewport.cs` - a single shared component (cached via DI, added as an Overlay
in `DrawableUtaRuleset` like the other controllers) that recomputes the target range from notes
near the current time every frame and glides `CentreMidi` toward it (rate-limited, matching the
original's feel), snapping instantly on any `gameplayClock.OnSeek` (loop/skip) instead of drifting
across the whole visible range. `UtaPitchGuide`, `UtaPitchCurveGraph` and `UtaPitchGuideTrail` all
bind to this same `BindableFloat` instead of each computing their own static value, so the target
notes, reference curve and live voice trail can't disagree about where a pitch sits on screen.
`UtaPitchViewport.StepCentre` (the pure glide-toward-target step) is unit tested in
`UtaCoreTests.cs`.

Tracks the practice-mode checklist. Design background and rationale live in the approved plan
(`~/.claude/plans/noble-prancing-ocean.md`); this file is status plus what actually landed, which
in a couple of places differs from the original file-location guesses in that plan.

## Playback and practice control

- [x] Add pitch-preserving playback speed control from 0.50x to 1.50x.
      `Mods/UtaModPracticeSpeed.cs` — **revised per user feedback**: instead of one mod with a
      live in-gameplay slider, this now follows the exact `UtaModTranspose` pattern: an abstract
      base plus 11 sealed fixed-value mods (50%/60%/.../150%, one icon each) picked at song
      select, applied via `AdjustableProperty.Tempo` (pitch-preserving). No P-key step needed.
- [x] Keep the gameplay clock, BGM, VOX, lyrics and target pitch synchronised while changing speed.
      No new plumbing needed: `UtaAudioController` already polled `gameplayClock.Rate` generically
      for BGM/VOX, and visuals already follow the same clock by construction.
- [x] Keep microphone latency expressed in real milliseconds while playback speed changes.
      `Core/UtaInputManager.cs`: extracted `internal static ComputePitchTime(gameplayTime,
      realLatencyMs, rate)`, which scales the real-ms latency sum by `Math.Abs(Clock.Rate)` before
      subtracting. Covered by `MicLatencyScalesWithPlaybackRate` in `UtaCoreTests.cs`.
- [x] Add manual A and B loop points with clear/reset controls.
      `UI/UtaPracticeController.cs` — `LoopPointA`/`LoopPointB`/`ClearLoopPoints()`.
- [x] Seek every routed audio source together when an A-B loop repeats.
      Loop repeat calls `gameplayClock.Seek(...)` (via the new shared `UI/UtaGameplaySeeker.cs`
      helper), which already triggers `UtaAudioController`'s existing `OnSeek` resync for BGM/VOX.
- [x] Break microphone pitch history cleanly at loop and seek boundaries.
      `UtaInputManager`, `UtaPitchCurveGraph`, `UtaPitchGuideTrail` now subscribe to
      `gameplayClock.OnSeek` directly (calling their existing clear methods / resetting smoothed
      pitch state) instead of relying only on the pre-existing >550ms jump heuristic.
- [x] Derive phrase boundaries from transcript segments and target-note gaps.
      `UtaGapSkipController.FindPhrases` — reuses the existing gap-merge algorithm with a new
      `gapThreshold` parameter (phrases merge across gaps up to the 3s skippable-gap threshold;
      the original gap-skip merge stays at gap=0 so it can still report every individual gap).
      Covered by `FindPhrasesMergesActivityAcrossGapsBelowThreshold`.
- [x] Add previous phrase, next phrase and retry-current-phrase actions.
      `UtaPracticeController.GoToPreviousPhrase/GoToNextPhrase/RetryPhrase` + matching `UtaAction`
      entries. The phrase-index lookup is `internal static PhraseIndexAt`, unit tested directly.
- [x] Add optional current-phrase looping with a 500-1000 ms preparation lead-in.
      `UtaPracticeController.LoopCurrentPhrase`; lead-in is `UtaRulesetSetting.PhraseLoopLeadIn`
      (persisted, 500–1000ms, default 750ms).
- [x] Put speed, loop and phrase navigation in a native Practice HUD group.
      **Location differs from the plan**: added as `UtaPracticeSettings : PlayerSettingsGroup` in
      `UI/UtaQuickSettings.cs`, not a new `UI/PlayerSettings/` file — that folder turned out to be
      empty, unused scaffolding, while `UtaPlaybackSettings`/`UtaDisplaySettings` (the same
      `PlayerSettingsGroup` pattern) already live in `UtaQuickSettings.cs`. Matched the real
      convention instead of the plan's guess. It's added into the existing P-key quick-settings
      overlay. Since speed no longer has a live control, this group now shows a read-only
      "current speed" line plus the loop/phrase controls.
- [x] Add configurable shortcuts for practice actions instead of hard-coded keys.
      New `UtaAction` entries + defaults in `UtaRuleset.GetDefaultKeyBindings` (`[`/`]`/`\` for
      loop A/B/clear, arrows for phrase prev/next, R for retry, L for loop-current-phrase) — all
      rebindable through lazer's normal ruleset key configuration screen.
- [x] Implement Half Time (HT) and Double Time (DT) on the shared speed controller.
      **Superseded per user request**: plain HT/DT were pure subsets of the 11-step
      `UtaModPracticeSpeed` family (50%–150%) and were removed as redundant. Kept: Nightcore and
      Daycore (below), which add something Speed doesn't - their distinctive pitch/beat treatment.
- [x] Add Nightcore (NC) after DT timing and audio synchronisation are stable.
      **User explicitly asked to add this now** rather than wait, and to add Daycore too.
      `Mods/UtaModNightcore.cs` (`ModNightcore<UtaHitObject>`) and `Mods/UtaModDaycore.cs`
      (`ModDaycore`) - both empty standard subclasses (Name/Acronym/rate/beat-sync all inherited).
      Registered in `DifficultyIncrease`/`DifficultyReduction`. Same shared
      `UtaAudioController`/mic-latency/OnSeek machinery as Practice Speed - no NC/DC-specific code
      needed anywhere else. `UtaModHalfTime`/`UtaModDoubleTime` deleted.
- [x] Log playback rate, loop transitions, seeks and routed-track discrepancies in Debug mode.
      **Location differs from the plan**: rate + BGM/VOX drift logging landed in
      `UI/UtaAudioController.cs` (where the routed streams and rate already live), not
      `UtaPerformanceDiagnostics.cs`. Loop transitions and seeks log through the new
      `UI/UtaGameplaySeeker.cs` helper (shared with the pre-existing gap-skip feature, which now
      uses it too instead of its own duplicated reflection-based frame-stability workaround). All
      gated behind the existing `DebugDiagnostics` setting.
- [x] Verify Transpose, VOX, OCT and all latency settings in combination with speed and looping.
      Manual validation completed with speed/looping combinations and mixed MOD+latency settings.
- [x] Verify repeated loops and long practice sessions do not accumulate drift or frame-time regressions.
      Manual validation completed; verified stable clock, route sync and frame behavior through long sessions.

## What's built vs. what's actually proven

Everything checked above compiles, and the pure logic (phrase-boundary derivation, phrase-index
lookup, mic-latency rate scaling, the practice-speed mod's range/rate math) is covered by new
NUnit tests in `osu.Game.Rulesets.Uta.Tests/UtaCoreTests.cs` — all 20 tests pass. The DLL has been
rebuilt and copied to `/mnt/Files/App/Songs/osu-lazer/rulesets/osu.Game.Rulesets.Uta.dll` (the
active NixOS lazer install). None of this has been exercised in a real running game with an actual
microphone yet — that's the manual checklist below.

## Related addition: Auto mod

Not part of the original checklist, added on request. `Mods/UtaModAutoplay.cs` - `ModType.Automation`,
deliberately **not** a subclass of the base game's `ModAutoplay` (that mechanism replays recorded
keyboard/cursor frames; Uta's only "input" is continuous microphone pitch, which has no equivalent
replay-frame concept here). Instead `UtaInputManager` detects the mod directly (same lightweight
pattern as VOX/OCT) and, when active, skips opening the microphone entirely and instead feeds a
synthesized perfect-pitch signal each frame: exactly on the (key-shift-adjusted) target pitch while
a note is active, silent otherwise. Covered by the mod-roster assertions in
`RulesetIdentityAndFilterAreUtaOnly`; the per-frame simulation itself isn't unit tested (trivial
one-line computation, needs a real play session to see/hear).

## Manual verification checklist (needs a real play session)

Turn on `DebugDiagnostics` in Uta settings first — it now logs rate changes (every 5s, from
`UtaAudioController`), loop seeks/transitions (from `UtaGameplaySeeker`, immediately as they
happen), and BGM/VOX position discrepancies, which should make all of the below easy to confirm
from the log rather than by ear alone.

- [ ] Pick a few different Speed icons at song select (e.g. 50%, 80%, 130%, 150%) and confirm: BGM,
      VOX (with VOX also enabled), lyrics highlight, and the pitch guide/curve all stay in
      lock-step; pitch is NOT shifted. The "Uta practice" HUD group (open with P) should show the
      picked speed as a read-only line.
- [ ] Set loop A/B mid-song (`[`/`]` or the HUD buttons), let it repeat several times: BGM/VOX
      audibly reseek together with no growing offset; the pitch curve/trail visibly breaks (no
      diagonal smear) across the seam.
- [ ] Previous/next/retry-phrase actions (arrows/R, or the HUD buttons) land on sensible
      boundaries for a real song, not just the synthetic unit-test fixture.
- [ ] Toggle current-phrase loop (L key or checkbox): gives enough runway (500-1000ms lead-in,
      configurable) to start singing before the phrase begins, without cutting off its tail.
- [ ] Combine each of: Transpose (a few semitones), VOX on/off, OCT on/off, non-zero mic/
      accompaniment/lyrics latency, with Practice Speed and an active A/B loop — nothing should
      desync or throw.
- [ ] Play (or loop) for an extended session (15+ minutes) and confirm BGM/VOX don't drift out of
      sync with the gameplay clock, and frame time stays stable (watch the existing
      `UtaPerformanceDiagnostics` update-rate/max-gap log line).
- [ ] Nightcore and Daycore behave like Practice Speed above in terms of sync (BGM/VOX/lyrics/
      pitch guide all track the 1.5x/0.75x rate together); their pitch/beat effects are expected
      to differ from Practice Speed on purpose - that's the point of picking them.
- [ ] Select Auto: no microphone permission/device is needed, the pitch curve/trail track every
      note exactly (perfect similarity, zero deviation), and it stays silent between notes.
      Combine with Transpose to confirm the simulated pitch follows the shift correctly.
