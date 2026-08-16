# uta! performance archive v1

## Purpose

uta! uses a dual-storage model:

```text
lazer ScoreInfo
    native total score, accuracy, rank, combo, judgement counts, MODs and date

uta! performance archive
    Pitch replay, note/phrase analysis, recording, waveform and engine metadata
```

The external archive does not replace lazer's local score history. If an archive is
moved or deleted, the native score remains visible; only detailed replay and recording
features become unavailable.

## Root layout

The user chooses a performance root directory. Recording remains opt-in.

```text
<UtaPerformanceRoot>/
├── index-v1.json
└── performances/
    └── <performance-guid>/
        ├── performance.json
        ├── pitch-replay.jsonl.br
        ├── take.wav                 # optional
        ├── waveform.bin             # optional cache
        └── complete
```

`performance.json` is the source of truth. `index-v1.json` is a rebuildable query
cache.

## Identity and native-score link

Each archive stores:

- `performance_id`: uta!'s immutable ID;
- `lazer_score_id`: the corresponding `ScoreInfo.ID`, when available;
- `lazer_score_hash`: optional post-import validation value;
- package ID, package revision and beatmap hash.

History lookup should use `ScoreInfo.ID`, not title/date/score fuzzy matching.

## Manifest contents

The implemented `UtaPerformanceManifest` records:

- schema and scoring-engine versions;
- song/package identity;
- native score ID/hash;
- total and component metrics;
- Perfect/Great/Good/Bad/Miss counts;
- immutable per-note summaries and a reserved per-phrase summary list;
- High/Low/Unstable/Inaccurate/LowCoverage counts;
- Transpose, OCT, rate, latency, sampling, scoring-bin, epoch and gain snapshot;
- comparable/ineligible state and stable invalidation reason identifiers;
- deterministic positive/advice message identifiers;
- relative asset file names;
- SHA-256 checksums.

Every path is a simple portable relative file name. Both Unix and Windows
separators, drive-colon forms, dot segments and NUL are rejected before access.

## Pitch replay

`pitch-replay.jsonl.br` is Brotli-compressed JSON Lines. Each compact record contains:

```json
{"t":10000,"p":6904,"c":842,"r":-237,"v":true,"e":0}
```

- `t`: song time in microseconds;
- `p`: MIDI cents;
- `c`: clarity permille;
- `r`: RMS dB in tenths, nullable;
- `v`: voiced flag;
- `e`: timeline epoch.

The replay contains standardised scoring frames, not raw microphone audio. It can
redraw the pitch curve and rerun the matching scoring-engine version.

## Recording

A take recorder writes an ordinary WAV/FLAC file beside the Pitch replay. Audio is
never embedded as base64 JSON. The archive foundation already accepts the optional
recording stream and waveform cache; live capture/writer wiring remains a later PR.

The manifest records container, sample format, sample rate, channels, song-time
origin, calibrated latency, input gain and signal stage. The agreed stage is
`post_input_gain_pre_monitor`. Recording is disabled by default and must have a
persistent visible state while active.

Formal performances normally have one monotonic take. Practice loops should use
separate attempt directories rather than one discontinuous WAV.

## Atomic write protocol

`UtaPerformanceArchiveWriter` implements:

1. create `.partial-<id>-<nonce>` under `performances/`;
2. write Pitch replay and optional recording;
3. close streams and calculate checksums;
4. write `performance.json.tmp`;
5. rename it to `performance.json`;
6. write `complete`;
7. atomically rename the partial directory to `<performance-id>`;
8. rebuild `index-v1.json` using a temporary file and replace.

Every referenced asset receives a SHA-256 checksum. Recording metadata and the
recording file must be present together.

Only directories containing `complete` are indexed. Stale partial directories can be
removed by recovery after a configurable age.

Archive failure must never block native `ScoreInfo` persistence.

## Query model

`UtaPerformanceLibrary` provides in-memory lookup by:

- performance ID;
- lazer score ID;
- package ID.

It scans manifests rather than trusting the index blindly. `UtaPerformanceScoreLinker`
atomically adds the final native `ScoreInfo.ID` and score hash after lazer import, then
rebuilds the index best-effort. A future UI layer can add a `FileSystemWatcher`, but
the index remains a cache and can always be rebuilt.

## Historical playback

Three modes are expected:

1. **Pitch replay**: redraw historical pitch, grades and phrase analysis.
2. **Take playback**: play the optional microphone recording alone or mixed with BGM,
   packaged vocals and original vocals.
3. **Scoring replay**: feed saved frames into the recorded engine version to reproduce
   the original judgement stream.

A newer engine may offer explicit reanalysis:

```text
Original v2 score:       914,230
Reanalysed with v3:      921,880
```

The original result remains immutable.

## Privacy and retention

Recommended defaults:

- Pitch replay: user-configurable auto-save;
- microphone recording: off;
- failed/partial files: automatic cleanup;
- archive deletion: allow separate deletion of analysis, Pitch replay and recording;
- storage UI: show total size, per-song size, date filters and retention policy.
