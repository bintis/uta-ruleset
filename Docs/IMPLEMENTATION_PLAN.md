# implementation status and migration plan

## Baseline

This package is generated against uta-ruleset `main` commit
`d0bac5ce3c0441877ef9cac7ec4e01b11e7d545f` and the osu!lazer
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
- passive and Fail health processor primitives;
- filesystem performance manifest, compressed Pitch replay, optional recording and
  waveform assets, atomic writer, reader, index, recovery, score linker and library;
- unit tests and design documentation.

## What is deliberately not activated

The foundation is compiled into the ruleset but is not made authoritative yet. The
following gameplay/UI changes still have to be connected as coordinated PRs:

- publish raw microphone analysis frames into `UtaCaptureFrameQueue`;
- use `UtaGameplayTimelineMapper` from the shared playback/seek/rate controller;
- drain mapped frames into `UtaStreamingScoringSession` on the gameplay thread;
- populate `UtaJudgementResult` from committed note scores in `DrawableUtaHitObject`;
- return `UtaScoreProcessor` and `UtaPassiveHealthProcessor` from `UtaRuleset`;
- expose `UtaModFail` in the MOD list;
- add live score/current-note HUD components;
- save/query performance archives from gameplay and results;
- implement microphone recording and the bounded WAV/FLAC writer.

These changes are intentionally not partially activated. Returning
`UtaScoreProcessor` before drawable results carry Uta fixed-point units would create
valid-looking but incorrect zero-unit scores.

## Changes from the previous prototype

| Previous prototype | Current standard |
|---|---|
| Perfect / Good / High / Low / Unstable / Miss as one enum | Perfect / Great / Good / Bad / Miss grade plus independent fault flags |
| High/Low mapped as score grades | Bad maps to native `Meh`; High/Low remain diagnostics |
| only Perfect/Good continued combo | Bad continues native combo; a separate accurate streak breaks on Bad |
| double-valued weights and profile totals | quantised fixed-point units and permille qualities at the scoring boundary |
| stability/technique bonus could reward stable off-pitch singing | per-note pitch-quality gate |
| Technique profile gave zero credit to short notes | short/non-technique notes fall back to Faithful within the Technique profile |
| uta.song 0.1 note kinds only | nullable MIDI and all 0.1/0.2 note kinds |
| long-tone technique only | long tone plus detrended periodic-vibrato quality |
| latest-only Pitch callback path | separate latest-only display mailbox and bounded formal scoring queue |
| direct `Time.Current - latency` mapping | segmented monotonic capture-time mapper with rate/seek epochs |
| design-only persistence | implemented atomic performance-folder archive foundation |
| no Bad equivalent | explicit Bad grade with native `Meh` compatibility |

## Suggested branch

```text
agent/scoring-v2-foundation
```

## Activation PR 1 — realtime bridge

Modify only the microphone and clock data path:

1. enqueue every accepted raw analysis window into the provided bounded queue;
2. retain the current latest-only mailbox exclusively for display smoothing;
3. publish pause, resume, rate and seek anchors into the provided timeline mapper;
4. map capture-window centres and calibrated real-time latency to song time;
5. drain frames and advance `UtaStreamingScoringSession` on the gameplay thread;
6. mark the session non-comparable if the formal queue overflows;
7. prove batch/stream equivalence at 0.5x, 1.0x and 1.5x.

Do not add HUD or native result application in this PR.

## Activation PR 2 — native judgements and score

Land these changes together:

- pitch-scored `UtaNote.CreateJudgement() => UtaJudgement`;
- ignored/non-pitch notes retain `IgnoreJudgement`;
- drawable creates and populates `UtaJudgementResult`;
- `UtaRuleset.CreateScoreProcessor() => UtaScoreProcessor`;
- default `UtaPassiveHealthProcessor`;
- native apply/revert, autoplay simulation, completion and rank tests.

## Activation PR 3 — live HUD and current results

- score, composite rating, raw Pitch accuracy and coverage;
- native combo and accurate streak;
- current-note grade, fault and cents bias;
- current-session phrase/result panels;
- no independent score calculation in drawables or HUD.

## Activation PR 4 — performance history

- performance-root configuration and storage diagnostics;
- write Pitch replay and `performance.json` at completion;
- preserve native score import when archive writing fails;
- link the committed archive to `ScoreInfo.ID`/hash after import;
- inject `UtaPerformanceLibrary` into the results panel;
- historical Pitch playback and engine-version-aware reanalysis.

## Activation PR 5 — recording

- capture after input gain and before monitor routing;
- bounded background PCM writer;
- explicit visible recording state and opt-in default;
- `take.wav`/FLAC and recording metadata in the same performance directory;
- historical solo/mixed playback and storage cleanup controls.

## Later calibration PRs

- labelled vibrato corpus and threshold versioning;
- phrase-relative RMS expression report and anti-AGC diagnostics;
- Fail MOD health balancing from measured score distributions.
