# implementation status and migration plan

## Baseline

This package is generated against uta-ruleset `main` commit
`72cfb9f0fbd4142f44fa7253a093978173c3a6c0` and the osu!lazer
`2026.804.2-lazer` gameplay/scoring API.

## What this package implements

- uta.pitch engine version 2;
- fixed 20 ms resampling and deterministic integer input contract;
- fixed-point scoring units and additive multi-profile numerators;
- Perfect / Great / Good / Bad / Miss grades;
- independent High / Low / Unstable / Inaccurate / LowCoverage faults;
- native `HitResult` mapping, including Bad → `Meh`;
- native-combo and separate accurate-streak semantics;
- pitch-gated Faithful / Stable / Technique profiles;
- long-tone and deterministic vibrato analysis;
- report-only RMS expression analysis;
- segmented monotonic-to-song-time mapping with timeline epochs;
- a bounded, observable microphone-to-gameplay scoring queue;
- streaming scoring session with a capture watermark;
- `UtaJudgement`, `UtaJudgementResult`, `UtaScoreProcessor` primitives;
- passive and 评分模式 health processor primitives;
- filesystem performance manifest, compressed Pitch replay, optional recording and
  waveform assets, atomic writer, reader, index, recovery, score linker and library;
- unit tests and design documentation.

## Runtime activation status

The scoring foundation is authoritative in gameplay in this integration:

- every accepted microphone analysis window enters `UtaCaptureFrameQueue`;
- capture centres are mapped by `UtaGameplayTimelineMapper` on the gameplay thread;
- `UtaStreamingScoringSession` commits deterministic whole-note scores;
- `DrawableUtaHitObject` emits populated `UtaJudgementResult` instances;
- `UtaRuleset` returns `UtaScoreProcessor` and passive default health;
- `UtaModScoringMode` is the sole switch for judgements, scoring HUD and
  note-driven health/failure;
- `UtaModRecording` is the sole switch for post-gain microphone PCM capture;
- realtime note commits use bounded note-local frame windows;
- gameplay writes the independent performance archive when scoring or recording
  is selected;
- the results statistics panel queries history by `ScoreInfo.ID`.

The following remains intentionally outside this patch:

- waveform generation;
- full song-clock historical playback and audio mixing;
- corpus-based vibrato/评分模式 health balance calibration;
- scored expression. RMS remains report-only.

See `SCORING_RUNTIME_INTEGRATION.zh-CN.md` for the active data flow and operational
contracts.

## Changes from the previous prototype

| Previous prototype | Current standard |
|---|---|
| Perfect / Good / High / Low / Unstable / Miss as one enum | Perfect / Great / Good / Bad / Miss grade plus independent fault flags |
| High/Low mapped as score grades | Bad maps to native `Meh`; High/Low remain diagnostics |
| only Perfect/Good continued combo | Bad continues native combo; a separate accurate streak breaks on Bad |
| double-valued weights and profile totals | quantised fixed-point units and permille qualities at the scoring boundary |
| stability/technique bonus could reward stable off-pitch singing | per-note pitch-quality gate |
| Technique profile gave zero credit to short notes | short/non-technique notes fall back to Faithful within the Technique profile |
| pitch-only note targets | nullable MIDI and all current vocal-chart note kinds |
| long-tone technique only | long tone plus detrended periodic-vibrato quality |
| latest-only Pitch callback path | separate latest-only display mailbox and bounded formal scoring queue |
| direct `Time.Current - latency` mapping | segmented monotonic capture-time mapper with rate/seek epochs |
| design-only persistence | implemented atomic performance-folder archive foundation |
| no Bad equivalent | explicit Bad grade with native `Meh` compatibility |

## Suggested branch

```text
agent/scoring-runtime-integration
```

## Historical staging plan — realtime bridge (implemented here)

Modify only the microphone and clock data path:

1. enqueue every accepted raw analysis window into the provided bounded queue;
2. retain the current latest-only mailbox exclusively for display smoothing;
3. publish pause, resume, rate and seek anchors into the provided timeline mapper;
4. map capture-window centres and calibrated real-time latency to song time;
5. drain frames and advance `UtaStreamingScoringSession` on the gameplay thread;
6. mark the session non-comparable if the formal queue overflows;
7. prove batch/stream equivalence at 0.5x, 1.0x and 1.5x.

Do not add HUD or native result application in this PR.

## Historical staging plan — native judgements and score (implemented here)

Land these changes together:

- pitch-scored `UtaNote.CreateJudgement() => UtaJudgement`;
- ignored/non-pitch notes retain `IgnoreJudgement`;
- drawable creates and populates `UtaJudgementResult`;
- `UtaRuleset.CreateScoreProcessor() => UtaScoreProcessor`;
- default `UtaPassiveHealthProcessor`;
- native apply/revert, autoplay simulation, completion and rank tests.

## Historical staging plan — live HUD and current results (implemented here)

- score, composite rating, raw Pitch accuracy and coverage;
- native combo and accurate streak;
- current-note grade, fault and cents bias;
- current-session phrase/result panels;
- no independent score calculation in drawables or HUD.

## Historical staging plan — performance history (implemented here)

- performance-root configuration and storage diagnostics;
- write Pitch replay and `performance.json` at completion;
- preserve native score import when archive writing fails;
- link the committed archive to `ScoreInfo.ID`/hash after import;
- inject `UtaPerformanceLibrary` into the results panel;
- historical Pitch playback and engine-version-aware reanalysis.

## Recording status — implemented in 0.6/0.7

- capture after input gain and before monitor routing;
- bounded background PCM writer;
- explicit visible recording state through the `Recording` (`REC`) MOD;
- `take.wav` and recording metadata in the same performance directory.

Historical solo/mixed playback and storage cleanup controls remain follow-up work.

## Later calibration PRs

- labelled vibrato corpus and threshold versioning;
- phrase-relative RMS expression report and anti-AGC diagnostics;
- 评分模式 MOD health balancing from measured score distributions.
