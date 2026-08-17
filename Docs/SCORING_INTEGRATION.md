# osu!lazer scoring integration plan

> Runtime status (main base `72cfb9f0fbd4142f44fa7253a093978173c3a6c0`): the
> realtime queue/timeline bridge, native judgement and score processor, Uta HUD,
> performance-folder save/query, result panel and explicit `评分模式` scoring/health MOD are
> activated by the runtime integration patch. PCM/WAV recording is available
> through the explicit `Recording` MOD; full song-clock historical playback remains follow-up work. See
> `SCORING_RUNTIME_INTEGRATION.zh-CN.md`.

## Baseline

This design targets:

- uta-ruleset `main` commit `72cfb9f0fbd4142f44fa7253a093978173c3a6c0`;
- osu!lazer `2026.804.2-lazer` runtime/API behaviour;
- uta.pitch scoring engine v2;
- uta.song 0.1 and 0.2 note kinds.

The foundation primitives were previously dormant. This runtime integration activates
them as one coordinated change so drawable results, native score units, health and
persistence cannot enter a partially-wired state.

## Architectural rule

There must be one authoritative scoring path:

```text
microphone samples
→ pitch detector
→ capture-time mapper
→ quantised UtaScoringFrame
→ UtaStreamingScoringSession
→ UtaNoteScore
→ UtaJudgementResult
→ DrawableRuleset.NewResult
→ HealthProcessor + UtaScoreProcessor + GameplayState
```

The pitch guide, note colours, HUD and result page consume this state. They do not
calculate independent scores.

lazer already forwards `DrawableRuleset.NewResult` to the health processor, score
processor and gameplay state, and forwards result reverts to both processors. Uta
should use that native event flow rather than create a second score bus.

## 1. Realtime microphone to scorer

### Capture thread

`UtaMicrophoneHandler` should produce raw analysis frames without touching drawable or
score state:

```text
UtaPitchFrame
- Hertz
- Clarity
- RMS
- ArrivalTimestamp
- WindowDurationMilliseconds
```

Use two consumers:

```text
PitchDetected
├── bounded FIFO queue → formal scoring/archive path
└── latest-only mailbox → smoothed pitch display
```

The existing latest-only strategy remains appropriate for rendering. It is not
appropriate for scoring because dropped intermediate windows would change duration.

The supplied `UtaCaptureFrameQueue` is bounded. When pressure occurs, it rejects the
new formal frame and increments an observable counter; integration must mark the
performance non-comparable. Never grow memory without limit and never silently
replace old score frames with the newest frame.

### Capture-time mapping

`ArrivalTimestamp` describes real monotonic time, not song time. Calculate the centre
of the analysed window first:

```text
captureCentreMonotonic
    = ArrivalTimestamp
    - WindowDuration / 2
    - calibrated microphone latency
```

Then map the monotonic timestamp through the supplied
`UtaGameplayTimelineMapper`, which stores segmented gameplay anchors:

```text
segment:
- monotonic anchor
- song-time anchor
- playback rate
- running/paused state
- timeline epoch
```

This order matters. Microphone latency is real milliseconds; song time changes at HT,
DT or arbitrary practice rates. Subtracting latency directly from `Time.Current`
without rate-aware mapping is incorrect.

Start a new segment when:

- playback rate changes;
- pause/resume changes;
- a seek or loop causes a time discontinuity;
- gameplay restarts.

A backward seek or loop starts a new epoch and makes a formal score ineligible unless
lazer's rewind path has reverted every affected native judgement.

### Gameplay thread

Only the gameplay update thread may mutate `UtaStreamingScoringSession`.

```text
capture worker
→ lock-free/bounded transfer
→ Schedule/update-thread drain
→ mapper.Map()
→ session.AddFrame()
→ session.AdvanceWatermark()
```

The watermark is mapped capture time, not render time. A note commits after its end
plus the configured `60 ms` delay so final detector windows can arrive.

## 2. UtaJudgement and UtaJudgementResult

Included primitives:

- `UtaJudgement` uses native range `Miss .. Perfect`.
- `UtaJudgementResult` stores all additive fixed-point units required for exact apply
  and revert.

Important fields:

```text
ScoringIndex
TimelineEpoch
Grade
Faults
MaximumUnits
PitchEarnedUnits
VoicedUnits
HitUnits
FaithfulEarnedUnits
StableEarnedUnits
TechniqueEarnedUnits
PitchAccuracyPermille
CoveragePermille
StabilityPermille
TechniqueQualityPermille
FinalRatingPermille
BiasCents
Accurate streak before/after values
```

The drawable must override `CreateResult()` to create `UtaJudgementResult`.
At commit time it calls `Populate(noteScore, epoch)` and applies the native result.

Native mapping:

| Uta grade | HitResult |
|---|---|
| Perfect | Perfect |
| Great | Great |
| Good | Good |
| Bad | Meh |
| Miss | Miss |
| Ignored | IgnoreHit |

High/Low/Unstable remain custom diagnostic flags and are not forced into native result
names.

## 3. Drawable note bridge

After realtime scoring is available, update `DrawableUtaHitObject`:

```csharp
protected internal override JudgementResult CreateResult(Judgement judgement)
    => new UtaJudgementResult(HitObject, judgement);

protected override void CheckForResult(bool userTriggered, double timeOffset)
{
    if (HitObject is not UtaNote note)
    {
        if (timeOffset >= 0)
            ApplyResult(HitResult.IgnoreHit);
        return;
    }

    if (timeOffset < commitDelayMilliseconds)
        return;

    UtaNoteScore score = scoringSession.GetRequiredCompletedNote(note.ScoringIndex);
    ApplyResult(static (result, state) =>
    {
        ((UtaJudgementResult)result).Populate(state.Score, state.Epoch);
    }, (Score: score, Epoch: scoringSession.TimelineEpoch));
}
```

Exact signatures should be adjusted against the active Nix osu! assemblies. The
important constraints are:

- result applied exactly once;
- result contains immutable additive units;
- note colour reads the same `UtaNoteScore`;
- no display-frame accumulation;
- rewind reuses lazer's result reversion.

The positive commit delay will appear as a native time offset. Uta-specific result
statistics should ignore timing-offset charts because this is a sustained-note
quality judgement, not a tap timing judgement.

## 4. UtaScoreProcessor

`UtaScoreProcessor` is included but should be returned from
`UtaRuleset.CreateScoreProcessor()` only after the drawable bridge is active.

It uses native `ScoreProcessor` for:

- judgement counts;
- native combo;
- mod multiplier application;
- rank binding;
- total-score/accuracy bindables;
- apply/revert lifecycle;
- `PopulateScore()`.

It overrides the additive score-change hooks to accumulate Uta fixed-point units.

### Live values

```text
PitchAccuracy       judged pitch-earned / judged maximum
Coverage            judged voiced / judged maximum
CompositeRating     best judged profile / judged maximum
TotalScore          best profile earned / full-song maximum × 1,000,000
MinimumAccuracy     current earned / full-song maximum
MaximumAccuracy     (current earned + remaining perfect units) / full-song maximum
```

`Accuracy` is set to the composite rating so native rank and stored `ScoreInfo.Accuracy`
match the advertised overall rating. Raw pitch accuracy remains available through
`UtaScoreProcessor.PitchAccuracy` and the performance archive.

### Simulation and maximum score

During `ApplyBeatmap()`, lazer simulates autoplay to derive maximum score/combo.
When `评分模式` is enabled, `UtaScoreProcessor.CreateResult()` creates a perfect
`UtaJudgementResult` for each scoring-enabled `UtaNote`, using the same deterministic
maximum-unit calculation as the live scorer. Without the mode, simulated and live
score values remain zero. The enabled simulated maximum remains `1,000,000`.

### Revert

Every custom field is additive. `RemoveScoreChange()` subtracts exactly what the
judgement added and restores accurate-streak before/after values. Do not recompute a
historical note from current settings during revert.

## 5. Activation sequence

Only after realtime equivalence tests pass:

1. `UtaNote.CreateJudgement()` returns `UtaJudgement` for scoring-enabled pitch notes
   and an ignored judgement otherwise.
2. `DrawableUtaHitObject.CreateResult()` returns `UtaJudgementResult`.
3. Drawable commits the completed session note result.
4. `UtaRuleset.CreateScoreProcessor()` returns `UtaScoreProcessor`.
5. `UtaRuleset.CreateHealthProcessor()` returns `UtaPassiveHealthProcessor` by default.
6. Add `UtaModScoringMode` to the available MOD list.

Activating only some of these steps would produce placeholder Perfect results or zero
custom units, so they should land together in the native-integration PR.

## 6. Native local score history

A non-legacy custom ruleset can still use lazer's local `ScoreInfo` history. The
native record can persist:

```text
TotalScore
TotalScoreWithoutMods
Accuracy
Rank
MaxCombo
Perfect/Great/Good/Bad(Meh)/Miss statistics
MODs
Date
Beatmap hash and ruleset identity
```

The default non-legacy import path does not create a legacy `.osr` archive. That does
not prevent `ScoreInfo` from entering Realm; it means custom replay frames are not
persisted by the legacy replay encoder.

Detailed Uta data therefore uses `PERFORMANCE_ARCHIVE.md`.

## 7. Performance archive and score link

On gameplay completion with `评分模式` and/or `Recording` selected:

1. populate native `ScoreInfo` through `UtaScoreProcessor` when scoring is enabled;
2. create a `UtaPerformanceManifest` and Pitch replay;
3. include the microphone recording only when `Recording` is selected;
4. write the archive atomically to the configured folder;
5. store `ScoreInfo.ID` as `lazer_score_id`;
6. after native import, optionally patch `lazer_score_hash` in the manifest.

Archive failure is non-fatal. Native score import continues and the UI reports that
only the detailed performance could not be saved.

Because current `Player.ImportScore()` exposes no ruleset-specific post-import
attachment hook, the external folder is the practical initial implementation. A small
upstream hook could later attach `uta-performance-v1.json` to `ScoreInfo.Files`, but
it is not required for history lookup.

## 8. Historical playback

Implement a ruleset-specific replay screen/controller rather than encoding Pitch as
legacy mouse coordinates.

```text
ScoreInfo.ID
→ UtaPerformanceLibrary.FindByLazerScoreId()
→ performance.json
→ pitch-replay.jsonl.br
→ UtaReplayPlaybackController
```

Playback can:

- redraw the historical pitch line;
- replay committed grades and analysis;
- rerun the recorded engine version;
- mix optional `take.wav` with BGM and packaged vocals.

A historical replay must not open the live microphone.

## 9. Live HUD

The existing `UtaHudController` already suppresses global HUD elements that have no
useful Uta meaning. Replace that suppression-only controller with Uta-specific
components bound to `UtaScoreProcessor` and the live session:

```text
Score               742,310
Overall             91.4%
Pitch               89.8%
Coverage            96.2%
Native combo        28
Accurate streak     17
Profile             Stable
Current note        BAD · HIGH · +82 cents
```

Rules:

- committed score/combo change only on native judgement;
- current-note preview may update at up to 20 Hz;
- preview never adds score, health or combo;
- history playback binds to archive state instead of a microphone session.

## 10. Result page

Override `Ruleset.CreateStatisticsForScore()` to return a Uta statistics item.
The panel queries `UtaPerformanceLibrary` by `ScoreInfo.ID`.

When the archive is present, show:

- overall component metrics;
- grade and diagnostic counts;
- phrase table;
- missed sections;
- Pitch replay button;
- recording playback/mix controls.

When absent, retain native score/rank/judgement display and show “Detailed performance
archive unavailable.”

## 11. Vibrato

The included detector is deterministic and fixed-grid. Before promoting it into a
stable score contract, validate it with labelled recordings covering:

- straight stable tones;
- natural vibrato across voice types;
- tremolo and amplitude modulation;
- random pitch wobble;
- scoops/falls near note boundaries;
- detector octave jumps.

Technique quality is capped and pitch-gated. Repeating vibrato does not award an
unbounded count bonus.

## 12. RMS expression

Restructure microphone capture before using expression metrics:

```text
raw capture
├── raw mono → RMS / peak / clipping / expression report
├── analysis copy → Pitch detector
└── monitor copy → InputGain → output route
```

Do not calculate expression from post-gain monitor samples. The included analyzer is
report-only and flags low dynamic range, clipping and possible AGC/compression.

A future scored expression profile requires calibration, noise-floor handling,
phrase-relative loudness and anti-gaming validation.

## 13. 评分模式 health

Default gameplay uses ignored judgements and performs no scoring. `UtaModScoringMode` enables native vocal judgements and supplies `UtaScoringModeHealthProcessor`.

The prototype health delta is note-driven:

```text
note share = note maximum units / song maximum units
health Δ   = note share × scale × (final note quality - neutral quality)
```

There is no continuous drain through intros, rests or breathing gaps. Base
`HealthProcessor` stores health before each judgement, so native reversion restores
health exactly.

Tune neutral quality and scale only after the score distribution is measured on a
recording corpus.

## 14. Required tests before activation

### Kernel

- fixed-grid batch/stream equivalence;
- frame order and sampling-density invariance;
- Transpose and OCT;
- every uta.song 0.1/0.2 note kind;
- Perfect/Great/Good/Bad/Miss thresholds;
- Bad + High/Low/Unstable diagnostics;
- Pitch-gated profiles;
- vibrato versus random wobble.

### Gameplay

- capture timestamps at 0.5x, 1.0x and 1.5x;
- pause/resume;
- seek/revert;
- A-B loop epoch isolation;
- detector backlog/drop behaviour;
- no double judgement;
- native score apply/revert symmetry;
- completion and results transition.

### Persistence

- native score remains when archive fails;
- atomic archive recovery;
- checksum failure;
- score-ID lookup;
- moved/deleted root folder;
- long recording and disk-full behaviour;
- recording-visible-state and deletion controls.

## 15. Recommended PR sequence

1. **Scoring v2 kernel, mapper, queue and archive foundation** — this package.
2. **Wire microphone capture and shared playback clock into the supplied mapper/queue.**
3. **Drawable judgement bridge + activate UtaScoreProcessor.**
4. **Uta live HUD and current-session results.**
5. **Performance-folder save, score linking, library query and historical Pitch playback.**
6. **Recording writer and historical audio mix.**
7. **Vibrato corpus calibration.**
8. **RMS expression report.**
9. **评分模式 health balancing.**
