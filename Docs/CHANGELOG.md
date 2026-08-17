# Changelog

This file records user-visible changes to `uta!`. Future work remains in the
[README roadmap](README.md#roadmap--todo).

## 0.5.1 - 2026-08-17

### Changed

- Bumped package version to `0.5.1`.
- Updated roadmap state for Scoring and feedback milestone, marking completed 0.5
  items and adding follow-up items under `0.5.1`.

### Fixed

- Cleaned up 0.5 rollout checklists and release metadata for 0.5.x delivery.

## 0.4.0 - 2026-08-16

### Added

- Added pitch-preserving playback speed control as fixed 0.50x to 1.50x practice
  rate options.
- Added practice A/B loop points, previous/next/retry phrase actions and
  current-phrase loop with configurable preparation lead-in.
- Added rebindable shortcut bindings for speed/loop/phrase practice actions.
- Added a native Practice HUD group for speed, looping and phrase controls.
- Added Daycore and Nightcore modulation options for playback variation.
- Added an Auto mod that synthesizes perfect pitch input for deterministic demo
  gameplay.

### Changed

- Kept gameplay clock, BGM, VOX, lyrics and target pitch synchronized while
  changing speed.
- Kept microphone latency compensation in real milliseconds and scaled it
  correctly when playback speed changes.
- Synchronized all routed audio on each A-B loop/seek boundary via shared seek
  plumbing.
- Cleared microphone pitch-history and visual continuity cleanly at loop and seek
  boundaries.
- Tightened phrase-boundary detection using transcript segment gaps and target-note
  gaps, with phrase-navigation and loop integration.
- Added shared playback-rate/debug diagnostics for loop transitions, seeks and route
  drift.

### Fixed

- Fixed practice viewport drift for pitch guide and trace visuals by binding guide,
  curve and trail to one shared adaptive pitch viewport.
- Fixed verification blockers by validating combined speed/loop/transpose/VOX/OCT
  combinations and extended practice flows with microphone latency presets in real
  sessions.

## 0.3.0 - 2026-08-15

### Added

- Added `-6` to `+6` semitone Transpose variants in lazer's native MOD selector.
- Added the opt-in `OCT` MOD for octave-equivalent scoring and pitch display.
- Added manual and automatic microphone latency calibration.
- Added independently adjustable accompaniment and lyrics latency controls.
- Added a configurable `10-40 ms` pitch sampling interval.
- Added a two-level microphone setup page with device selection, input gain,
  monitoring, routing and diagnostics.
- Added Debug logging for frame timing, memory, microphone analysis throughput,
  pitch-curve geometry and timeline discontinuities.

### Changed

- Kept BGM, vocals, target notes and pitch scoring synchronised while transposing.
- Made `VOX` an explicit opt-in for packaged original-vocal playback.
- Moved pitch analysis off the recording callback and bounded pending work so a
  slow update thread cannot grow an unbounded analysis queue.
- Separated latency-corrected scoring timestamps from immediate display timestamps,
  preserving 0.2.1's responsive live trace without losing scoring alignment.
- Restored the 0.2.1 pitch detector window, overlap, smoothing and `20 ms` curve
  sampling behaviour while retaining the new timing metadata.
- Made the installed Nix osu! `2026.804.2` build the local API and dependency
  source of truth, with a NuGet fallback for CI.

### Fixed

- Fixed Transpose MOD selection producing duplicate or invalid entries.
- Fixed mode conversion failures when switching to uta!.
- Fixed microphone history gaps caused by mixing compensated and uncompensated
  sampling timestamps.
- Fixed the microphone diagnostics page retaining the capture device after setup.
- Reduced repeated curve work and added direct diagnostics for long-session stalls.

## 0.2.1 - 2026-08-15

### Changed

- Rebuilt the former karaoke fork as a small, Uta-only osu!lazer playback ruleset.
- Removed the legacy editor, online services, custom skin system and unrelated
  karaoke infrastructure from the runtime scope.
- Adopted native lazer ownership of song select, video, artwork, clocks, pause,
  results and imported beatmap storage.

### Added

- Added validated `.utz` import through lazer's public file-import pipeline.
- Added word-timed lyrics with progressive highlighting.
- Added target notes, live microphone pitch detection, pitch feedback and rolling
  song/voice history curves.
- Added independent BGM, vocal and microphone-monitor routing with shared output
  buses per hardware device.
- Added Linux microphone capture through lazer's BASS runtime and packaged the
  official Linux x64 BASSFLAC add-on.
- Added native gap-skip prompts, remapped volume controls and a `P` quick-settings
  panel.
- Added lyrics, pitch-guide and original-vocals MODs plus configurable lyric and
  pitch-guide presentation.
- Added focused package, pitch, lyric, routing and ruleset tests.

## 0.1.0 - 2026-08-13

### Added

- Published the first playable uta-ruleset preview as a hard fork of
  `karaoke-dev/karaoke`.
- Added `.utz` import with packaged artwork, video, instrumental audio and optional
  guide/original vocal stems.
- Added microphone capture, monitoring, live pitch detection, lyrics, pitch scoring,
  gap skipping and karaoke-focused MODs.
- Added native lazer settings, volume HUD integration and Uta-only song filtering.

[0.5.1]: https://github.com/bintis/uta-ruleset/compare/v0.5.0...v0.5.1
[0.3.0]: https://github.com/bintis/uta-ruleset/compare/v0.2.1...main
[0.4.0]: https://github.com/bintis/uta-ruleset/compare/v0.3.0...main
[0.2.1]: https://github.com/bintis/uta-ruleset/compare/v0.1.0...v0.2.1
[0.1.0]: https://github.com/bintis/uta-ruleset/releases/tag/v0.1.0
