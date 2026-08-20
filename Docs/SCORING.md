# uta! scoring engine v2

This document defines the current scoring contract implemented by the files under
`osu.Game.Rulesets.Uta/Scoring`.

The design borrows the useful product idea behind karaoke precision scoring—separate
pitch, stability and technique feedback, then recognise more than one valid singing
style—without attempting to reproduce DAM's private thresholds, training data or
proprietary total-score formula.

## Status

The package contains a deterministic scoring kernel, a streaming wrapper, native
lazer integration primitives, vibrato analysis, report-only RMS analysis and a
filesystem performance archive.

The native primitives are intentionally **not activated automatically**. The current
runtime still needs the microphone-to-session and drawable-to-judgement wiring
specified in `SCORING_INTEGRATION.md` before `UtaScoreProcessor` should replace the
existing placeholder scoring path.

## Deterministic input contract

Determinism begins after microphone analysis. Given the same:

- scoring engine version;
- beatmap targets;
- MOD/settings snapshot;
- quantised pitch frames;
- timeline epoch;

uta! must produce exactly the same note results and aggregate score.

The scorer receives:

```csharp
UtaScoringFrame(
    long TimeMicroseconds,
    int PitchCents,
    ushort ClarityPermille,
    bool Voiced,
    int TimelineEpoch);
```

Time is integer microseconds, pitch is integer MIDI cents, and confidence is an
integer from 0 to 1000. Hardware-specific audio capture and pitch detection happen
before this boundary.

## Scoring grid

Pitch callbacks are not score events. All input is resampled to a global `20 ms`
song-time grid.

- Interpolation is allowed only between usable frames no more than `80 ms` apart.
- Otherwise the nearest frame may be used only within `40 ms`.
- A gap larger than those limits is unvoiced.
- Frames from different timeline epochs are never mixed.
- Duplicate timestamps are resolved deterministically.

This prevents microphone sampling interval, callback count, renderer FPS and short
CPU stalls from changing the score.

## Scoring targets

The current package understands all note kinds used by the `uta.song 0.3.x` vocal chart:

| Kind | Pitch-scored | Weight |
|---|---:|---:|
| Normal | Yes | 1x |
| Golden | Yes | 2x |
| Freestyle / GoldenFreestyle | No | 0 |
| Rap / GoldenRap | No | 0 |
| Spoken / GoldenSpoken | No | 0 |

A target is ignored when:

- it has no MIDI pitch;
- its kind is not pitch-scored; or
- its target confidence is below `0.500`.

Ignoring a note is not a miss and does not affect score or combo.

## Fixed-point weight units

Each scoring bin contributes deterministic integer units:

```text
units = overlap duration
      × note-boundary weight
      × target-confidence weight
      × note-kind multiplier
```

Boundary and confidence weights are quantised to permille before multiplication.
The beginning and end of a note use a linear soft edge of at most `60 ms` and at
most one quarter of the note duration. This reduces unfair deductions from
consonants and pitch-analysis windows spanning adjacent notes.

## Pitch similarity

Deviation is calculated in cents after Transpose. OCT folds deviation to the nearest
equivalent pitch class in `[-600, +600]` cents.

The v2 similarity curve preserves the existing uta! shape:

| Absolute deviation | Similarity |
|---:|---:|
| `0–35` cents | `1.000` |
| `35–75` cents | linearly `1.000 → 0.880` |
| `75–150` cents | linearly `0.880 → 0.300` |
| `150–250` cents | linearly `0.300 → 0.000` |
| over `250` cents | `0.000` |

A bin is a pitch hit when the absolute deviation is at most `75` cents.
Detector clarity is a voiced/unvoiced gate, not a continuous score bonus. Once a
frame passes the clarity threshold, a higher-clarity microphone does not receive
more points.

## Per-note metrics

For each target note:

```text
Pitch accuracy = pitch-earned units / maximum units
Coverage       = voiced units / maximum units
Hit ratio      = <= 75-cent units / maximum units
Pitch bias     = weighted median signed deviation
Raw stability  = exp(-(weighted MAD / 45 cents)^2)
```

Weighted median and weighted median absolute deviation are used instead of an
arithmetic mean and standard deviation so isolated octave errors do not dominate the
feedback.

### Vibrato

Vibrato is analysed on fixed-grid pitch deviation:

- longest contiguous voiced run;
- minimum duration `350 ms`;
- approximately `3.5–10 Hz`;
- approximately `15–100 cents` extent;
- at least two cycles;
- normalised autocorrelation threshold `0.55`.

The detector removes a linear centre trend before periodicity analysis and rejects
centre drift above `80 cents/second`. A high-quality periodic vibrato may recover
stability that would otherwise look like random pitch jitter. Vibrato count is not
directly added to score.

### Long tones and technique

Long-tone quality is available on notes at least `800 ms` long:

```text
LongToneQuality = sqrt(Coverage × Stability)
TechniqueQuality = max(LongToneQuality, VibratoQuality)
```

## Grade and diagnostic fault

The user-facing grade and diagnostic cause are separate.

### Grade

| Grade | Initial v2 condition | Native lazer result |
|---|---|---|
| Perfect | accuracy ≥ 0.940, hit ratio ≥ 0.860, coverage ≥ 0.850 | Perfect |
| Great | accuracy ≥ 0.860, coverage ≥ 0.750 | Great |
| Good | accuracy ≥ 0.700, coverage ≥ 0.600 | Good |
| Bad | coverage ≥ 0.350 but below Good | Meh |
| Miss | coverage < 0.350 | Miss |
| Ignored | non-scoring target | IgnoreHit |

`Bad` maps to lazer's `Meh`, which is the closest native “50/bad” result.

### Diagnostic faults

A note may additionally carry one or more flags:

- `High`: a Bad/Miss note has median bias greater than `+35 cents`;
- `Low`: a Bad/Miss note has median bias lower than `-35 cents`;
- `Unstable`: stability below `0.550`;
- `LowCoverage`: coverage below the Good threshold;
- `Inaccurate`: a Bad/Miss note without a stronger directional or stability cause.

Examples:

```text
Bad + High       (+82 cents)
Bad + Unstable   (bias +4 cents, stability 0.31)
Miss + LowCoverage
```

## Combo semantics

Two streaks are maintained:

- **Native combo** follows lazer semantics. Perfect, Great, Good and Bad continue it;
  Miss breaks it.
- **Accurate streak** continues only on Perfect, Great and Good; Bad and Miss break it.

The main score has no combo multiplier. This avoids making the same melody worth
more merely because an author split it into more notes.

## Multi-profile total score

Each note produces three additive profile numerators.

A pitch-quality gate prevents stable but clearly wrong singing from receiving a
large stability or technique bonus:

```text
gate(A) = clamp((A - 0.550) / 0.300, 0, 1)
Sg      = Stability × Coverage × gate(A)
Tg      = TechniqueQuality × gate(A)
```

Profiles:

```text
Faithful  = 0.940 A + 0.060 Sg
Stable    = 0.900 A + 0.100 Sg
Technique = 0.880 A + 0.060 Sg + 0.060 Tg
```

The final profile is the largest whole-performance numerator after adding note
contributions. Applying the gate per note prevents accurate sections from masking an
off-pitch note's technique bonus. On a note with no eligible long-tone or vibrato
section, its Technique-profile contribution falls back to Faithful rather than zero;
short-note passages are therefore neutral instead of penalising the entire profile.

```text
TotalScore = round(1,000,000 × best profile earned units / maximum units)
```

Profile accumulators, coefficients and persisted quality values use fixed-point
permille. Signal analysis may use floating-point internally, but every value crossing
into scoring/persistence is deterministically quantised.

## Analysis report

The deterministic report generator selects:

1. one strongest positive observation; and
2. one highest-priority actionable recommendation.

It returns stable message identifiers rather than hard-coded display strings so the
UI can localise them. Pitch direction, coverage, stability, Bad-note count, long
tones and vibrato are considered.

## RMS expression

`UtaExpressionAnalyzer` reports:

- voiced P10, median and P90 RMS;
- dynamic range;
- clipping ratio;
- possible AGC/heavy-compression behaviour.

RMS expression is **not part of v2 score**. Input gain, microphone distance, AGC and
hardware compression can change RMS without changing singing ability. It remains a
report-only metric until the capture path and calibration model are mature.

## Seek, loop and practice

A timeline epoch identifies one monotonic traversal. Frames from different epochs
are never combined.

- A formal performance should remain on epoch 0 and a monotonic song timeline.
- A-B loops and phrase retries create new practice attempts/epochs.
- Practice UI may show current and best attempts.
- It must not assemble a full score from the best bins across multiple attempts.

## Realtime transport and eligibility

`UtaGameplayTimelineMapper` stores rate/seek/pause anchors and maps the centre of each
analysis window from monotonic capture time into song time. Backward seeks and loops
start a new timeline epoch.

`UtaCaptureFrameQueue` is bounded and never silently replaces formal scoring frames.
An overflow increments an observable rejection counter. Integration must mark the
performance non-comparable and record the reason in the performance manifest rather
than pretend that a dropped-duration score is authoritative.

## Versioning

The scoring engine version in this package is `2`. Saved performance manifests must
record the engine version and settings snapshot. A future engine may reanalyse an old
Pitch replay, but must never silently overwrite the original score.
