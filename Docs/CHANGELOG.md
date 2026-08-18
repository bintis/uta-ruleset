# Changelog

This file records user-visible changes to `uta!`. Future work remains in the
[README roadmap](README.md#roadmap--todo).

## 0.8.0 - 2026-08-18

### Added

- Added an optional local-network mobile remote. Press `K` to open the
  overlay, start the listener and show a pairing QR code. The phone loads one
  embedded HTML page over HTTP and talks to the same host over WebSocket.
- Pairing tickets are 90-second, single-use credentials. Reconnects use a
  per-tab session secret. Desktop start is required before any controller is
  accepted; exiting gameplay stops the listener and revokes every ticket and
  session.
- Added a read-only spectator role. Spectators can watch lyrics, pitch and
  score, and may only send `ping`, `disconnect`, library search and a song
  request.
- The mobile client covers play, pause, seek, speed, A-B loops, phrase
  navigation, mixer, Transpose, VOX, OCT and latency controls, with the same
  bounds as the desktop UI. English, Chinese and Japanese are selectable on
  the page, and `prefers-reduced-motion` is honoured.
- Added a global song queue (`F8`) with search, add, reorder, play-now and
  skip. `N` skips to the next queued song. The `Immersive Queue` (`IQ`) MOD
  continues to the next queued song after results.
- Added a bounded, path-stripped import diagnostics view for failed `.utz`
  packages. Full exceptions stay in the lazer log.
- Auto now emits 20 ms synthetic analysis frames through the formal scoring
  path, so native judgements, the Score HUD and the results screen share one
  pipeline.
- Added ruleset-native skin lookup identifiers, accessible colour fallbacks,
  reduced-motion / particle-intensity settings, and video visibility, dim,
  blur and offset controls.

### Changed

- Historical configuration keys 0-22 are frozen. New settings are appended so
  an older `RulesetConfigManager` cannot reread a later value under the wrong
  numeric key. Microphone monitor output stays on its original key.
- Changing Transpose or OCT during a run starts a new timeline epoch, resets
  the streaming session and marks the performance non-comparable.
- Remote credentials are hashed in process only and are never written to
  ruleset configuration. The listener accepts loopback, RFC1918 and
  link-local clients; public addresses and cross-origin browser requests are
  rejected.

### Fixed

- PCM capture completion and disposal are now atomic and idempotent, so a
  producer/channel race cannot leak a rented buffer or drop the last block.
- Import failures no longer surface raw paths or stack traces in the user
  view.

### Validation

- Added pairing, replay-guard, private-network, spectator, command-bound,
  QR-finder and PCM-queue regression tests.
- CI verifies the embedded remote page is a single self-contained HTML file
  and rebuilds the WASM helper on the release-hardening workflow.
- Real-device LAN, firewall/URL-ACL and mobile-browser acceptance passed
  (pairing, private-network bind policy and the phone client on a live
  session). See [DEVICE-ACCEPTANCE.md](DEVICE-ACCEPTANCE.md).

### Known issues

- Skin lookups and video settings are present, but existing pitch/lyrics
  drawables are not yet fully replaced by those lookups, and the exact
  ruleset video drawable binding still needs a pass against the target lazer
  background/video hierarchy.
- The 0.7.2 microphone-monitor persistence issue is still open.

See [REMOTE-PROTOCOL.md](REMOTE-PROTOCOL.md) and
[REMOTE-SECURITY.md](REMOTE-SECURITY.md) for the local-network contract.

## 0.7.2 - 2026-08-18

### Fixed

- Fixed the Score HUD (`S`) getting permanently stuck hidden. Root cause:
  neither the Score HUD nor the new Practice HUD set `AlwaysPresent`, so once
  faded to `Alpha 0` a drawable becomes "not present" in osu!framework and
  drops out of the input queue - including the very keybinding meant to bring
  it back. This resolves the known issue tracked in 0.6.2.
- Removed a redundant time-based debounce on the Score HUD's toggle handler
  that could itself swallow a legitimate second key press landing inside the
  first press's 150ms fade - the key-binding container already only raises
  `OnPressed` once per physical key-down, so the debounce had nothing left to
  protect against.
- Fixed lazer's native volume HUD popping open (and eating scroll-wheel
  input) when opening the in-game settings panel (`O`). The panel's playback
  sliders share bindables with the remapped native volume meters, so their
  first lazy-load echoed into the meters; the panel now suppresses the
  native volume overlay and consumes scroll input itself while it is open.
- Fixed `UtaAudioRouter`'s per-device mixer cache (`getBus`) having no
  locking, a latent race if two routed sources for the same output device
  are created concurrently during component load.
- Fixed `UtaModNoFail` and `UtaModRelax` showing hardcoded Chinese MOD
  descriptions regardless of the player's selected language.

### Added

- Added a standalone Practice HUD (`P`), independent of the full settings
  panel. It is now gated behind a new `Practice` (`PR`) MOD - without it, `P`
  does nothing - and contains loop points, phrase navigation and a **live**
  pitch-preserving speed control.
- Replaced the 11 fixed-value `UtaModPracticeSpeed50`-`150` MODs with a
  single live speed slider in the Practice HUD, bound directly to lazer's own
  `MasterGameplayClockContainer.UserPlaybackRate` (the same mechanism lazer's
  built-in practice-speed control uses) so it can be changed mid-song instead
  of being fixed at song select. Added a Reset-speed button and a live
  "current speed" readout.
- Remapped `OpenSettings` from `P` to `O` to make room for the Practice HUD
  on `P`.
- Added Chinese/English/Japanese text for the Score HUD and Practice HUD,
  following lazer's own `Settings > General > Language` selection live
  (`osu.Game.Rulesets.Uta.Localisation`). This also fixes the Score HUD
  previously showing raw Chinese labels regardless of language.
- Added "Microphone monitor output" and its volume slider to the in-game
  settings panel's playback group - previously only reachable from the
  separate global ruleset settings page outside gameplay, so it could never
  actually be changed while testing routing live.
- Added a background dim/blur settings group to the in-game settings panel,
  replacing native `VisualSettings` (which also brought in combo-colour
  normalisation, storyboard, beatmap-skin and beatmap-colour controls that do
  not apply to uta!).
- Added extensive Debug-gated logging across the microphone handler, audio
  router, recording runtime and settings panels to speed up future
  diagnosis.

### Changed

- Removed native `VisualSettings` and `InputSettings` from the in-game
  settings panel; reordered its groups to Background, Playback, Display,
  Audio.

### Known issues

- Microphone monitor output does not reliably survive to the next play
  session - it applies live and correctly re-routes audio immediately, but
  the persisted setting can read back as unset next time gameplay starts.
  An explicit `config.Save()` on the settings panel closing did not resolve
  it. Tracked in the [README roadmap](README.md#roadmap--todo) for a fresh
  pass.

## 0.6.2 - 2026-08-17

### Changed

- Scoring is now active by default; the former `评分模式` (`SC`) MOD is
  replaced with `Relax` (`RX`), which opts back out into unscored practice.
- Moved `Auto` (`AT`) into the `Fun` MOD category.
- The Score HUD now shows the total score as a 0-100 scale (matching the
  results screen), and can be toggled in-game with `S`. Its screen corner is
  configurable in ruleset settings.

### Added

- Added explicit `No Fail` (`NF`) MOD.
- Added a 0-100 total score line to the Uta results-screen panel.

### Fixed

- Fixed `UtaGameplayScoringController` advancing its watermark from the raw
  capture timestamp instead of the same latency-adjusted time used to map
  microphone frames, which rejected nearly every frame as "late" and left
  every note scoring as an empty Miss.
- Fixed `DrawableUtaHitObject` expiring notes on the framework's initial
  `ArmedState.Idle` setup rather than only on a real Hit/Miss, which killed
  the object before its asynchronous judgement could ever arrive and left
  `ScoreProcessor.HasCompleted` permanently false (no results screen).
- Fixed an in-progress recording take being left orphaned under `staging/`
  with no archive when gameplay exits before the natural-end watcher runs.

### Known issues

- Pressing `S` to hide the Score HUD can leave it permanently hidden; a
  second `S` press does not always bring it back. Root cause not yet found -
  tracked in the [README roadmap](README.md#roadmap--todo) for 0.6.3.

## 0.7.0 - 2026-08-17

### Added

- Added the explicit `Recording` (`REC`) MOD. Microphone PCM is captured and
  saved only when this MOD is selected for the play.
- Added the explicit `评分模式` (`SC`) MOD as the sole switch for vocal
  judgements, live score HUD, note-driven health and score archives.

### Changed

- Removed the recording checkbox from ruleset settings; recording is now chosen
  per play from the native MOD selector.
- Renamed the former `Challenge` MOD and its public implementation types to
  `评分模式`.
- Normal karaoke play now uses ignored judgements and does not calculate or
  display a score unless `评分模式` is enabled.

### Fixed

- Fixed progressively worsening gameplay stutter and eventual freezing on long
  songs by scoring each completed note from a bounded local pitch-frame window
  instead of re-sorting and re-sampling the entire performance history.
- Cached the final full-performance calculation so archive and phrase
  finalisation do not repeat the same whole-song scoring pass.
- Added thread-safe snapshots for pitch replay data during archive finalisation.
- Preserved signed microphone-latency calibration in the formal scoring path.

### Validation

- Added regression coverage that completes 300 notes after preloading a long
  pitch stream and verifies that realtime per-note frame windows remain bounded.
- Added mode-gating tests for default ignored judgements, `评分模式`, and the
  `Recording` MOD.

## 0.6.0 - implementation candidate

### Added

- Activated Uta scoring through lazer native judgements, score processor and passive health flow.
- Added native ranking-screen Uta statistics without creating a second results screen.
- Added deterministic phrase summaries and vocal-range transpose recommendation.
- Added bounded post-gain/pre-monitor microphone recording to streaming PCM16 WAV.
- Added recording timeline segments, phrase-attempt storage, WAV export, take playback and A/B comparison primitives.
- Added persistent recording state and archive/storage integration.

### Validation

- Added deterministic recording, timeline, vocal-range and scoring-matrix tests.
- Device soak / hot-plug validation remains a release gate and is intentionally not marked complete.

## 0.5.0 - 2026-08-17

### Added

- Added deterministic per-note scoring from pitch similarity, voiced
  duration and confidence, without double-counting after seeks or loops.
- Added live score, accuracy and consecutive-hit feedback to the gameplay
  HUD.
- Added an overall grade plus per-phrase accuracy, pitch bias, stability and
  missed-section reporting.
- Added automatic vocal-range detection with a recommended Transpose value
  before play.
- Added note-driven health once it could be driven by the completed
  pitch-scoring pipeline.
- Added deterministic scoring tests using recorded Pitch frames and fixed
  gameplay timestamps.

### Changed

- Applied Transpose and OCT consistently to both live scoring and recorded
  score data.
- Kept scoring stable across pause, seek, playback-rate and A-B loop
  transitions.
- Wrote completed performances into lazer's native score and results flow.

### Validation

- Verified scoring combinations for Transpose, OCT, HT, DT, latency and
  phrase looping.

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

[0.8.0]: https://github.com/bintis/uta-ruleset/compare/v0.7.2...v0.8.0
[0.7.2]: https://github.com/bintis/uta-ruleset/compare/v0.6.2...v0.7.2
[0.5.1]: https://github.com/bintis/uta-ruleset/compare/v0.5.0...v0.5.1
[0.3.0]: https://github.com/bintis/uta-ruleset/compare/v0.2.1...main
[0.4.0]: https://github.com/bintis/uta-ruleset/compare/v0.3.0...main
[0.2.1]: https://github.com/bintis/uta-ruleset/compare/v0.1.0...v0.2.1
[0.1.0]: https://github.com/bintis/uta-ruleset/releases/tag/v0.1.0
