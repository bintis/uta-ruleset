# uta!

`uta!` is a karaoke game distributed as a custom ruleset for osu!lazer. It
turns `.utz` song packages into a native lazer gameplay experience, combining
timed lyrics, reference notes, live microphone Pitch and flexible audio
routing in one lightweight ruleset.

The project follows a simple development principle: provide the features a
karaoke game needs while keeping the ruleset as small and efficient as
possible. Wherever practical, uta! reuses osu!lazer's existing interfaces,
gameplay clock, media playback, song library and platform support instead of
building a second application stack.

Song editing is intentionally kept outside the ruleset. Songs, lyrics,
reference Pitch and package metadata are created and exported as `.utz` files
with **Uta Studio**; uta! is responsible for importing and playing those
packages inside osu!lazer.

See [CHANGELOG.md](CHANGELOG.md) for released version history.

## Current features

- Native `.utz` validation, drag-and-drop import and library integration through
  osu!lazer's public file-import API.
- Word-timed lyrics with progressive highlighting and configurable placement,
  typeface and scale.
- Reference notes, real-time microphone Pitch detection, singing history and
  clear visual feedback for whether the voice is high, low or on target.
- A colour-blind-friendly dark Pitch guide with distinct target, packaged-vocal
  and live-microphone layers.
- Independent BGM, packaged-vocal and microphone-monitor volume controls and
  output routing, with one shared mix bus per selected hardware device.
- Configurable microphone device, input gain, monitoring, analysis sampling and
  latency compensation through native ruleset settings.
- Independent accompaniment and lyrics latency adjustments available during
  gameplay for fast synchronisation corrections.
- Transpose support that shifts BGM, packaged vocals and reference notes
  together while preserving synchronisation.
- Optional Lyrics, Pitch Guide, Original Vocals and Octave Folding MODs using
  osu!lazer's native MOD interface.
- Explicit `评分模式` (`SC`) and `Recording` (`REC`) MODs: ordinary karaoke
  play remains unscored, while recording is enabled per play rather than by a
  persistent settings checkbox.
- Octave Folding can match equivalent notes across octaves without altering the
  detected microphone signal.
- Native skip prompts for long introductions, gaps between phrases and trailing
  silence.
- osu!lazer's volume overlay remapped to microphone monitor, BGM and packaged
  vocals during gameplay.
- Debug diagnostics for Pitch capture, analysis, rendering, routed audio and
  latency investigation.
- Linux microphone and FLAC support built on the BASS runtime shipped with
  osu!lazer.

## Project boundaries

This repository contains the gameplay ruleset, not a song editor or standalone
karaoke application. Uta Studio owns song authoring, while video, artwork, song
selection, the gameplay clock and results remain owned by osu!lazer. uta! adds
only the karaoke-specific playback, microphone, Pitch and audio-routing layers.

There is currently no online service or separate custom-skin package system.
Those areas are tracked explicitly in the roadmap rather than being added to
the runtime without a defined release target.

## Build and test

Requires .NET 8 and the locally installed Nix osu! package. The Nix installation
is the source of truth for osu! API and dependency versions; the currently
detected target is osu! `2026.804.2`. Resolve the active store path rather than
committing a Nix store hash:

```sh
OSU_BIN="$(readlink -f "$(command -v 'osu!')")"
OSU_ROOT="$(dirname "$(dirname "$OSU_BIN")")"
OSU_NIX_DIR="$OSU_ROOT/lib/osu-lazer"

dotnet build osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj -c Release \
  -p:OsuNixDir="$OSU_NIX_DIR"
dotnet test osu.Game.Rulesets.Uta.Tests/osu.Game.Rulesets.Uta.Tests.csproj -c Release \
  -p:OsuNixDir="$OSU_NIX_DIR"

mkdir -p "$HOME/.local/share/osu/rulesets"
cp osu.Game.Rulesets.Uta/bin/Release/net8.0/osu.Game.Rulesets.Uta.dll \
  "$HOME/.local/share/osu/rulesets/osu.Game.Rulesets.Uta.dll"
```

Copy `osu.Game.Rulesets.Uta.dll`, `libbassflac.so`, and `BASSFLAC.txt` from
`bin/Release/net8.0` into lazer's `rulesets` directory. The host installation
provides osu! and BASS; Uta bundles only the official BASSFLAC Linux x64 add-on
needed by the `.utz` FLAC audio contract.

Select `uta!` once after launch to register native `.utz` drag-and-drop import.
Imported packages are validated in memory and handed to lazer's beatmap manager
as a standard archive, so lazer owns storage and media decoding.

## Roadmap / TODO

### 0.4.0 - Playback and practice control

- [x] Add pitch-preserving playback speed control from `0.50x` to `1.50x`.
- [x] Keep the gameplay clock, BGM, VOX, lyrics and target pitch synchronised while changing speed.
- [x] Keep microphone latency expressed in real milliseconds while playback speed changes.
- [x] Add manual A and B loop points with clear/reset controls.
- [x] Seek every routed audio source together when an A-B loop repeats.
- [x] Break microphone pitch history cleanly at loop and seek boundaries.
- [x] Derive phrase boundaries from transcript segments and target-note gaps.
- [x] Add previous phrase, next phrase and retry-current-phrase actions.
- [x] Add optional current-phrase looping with a `500-1000 ms` preparation lead-in.
- [x] Put speed, loop and phrase navigation in a native `Practice` HUD group.
- [x] Add configurable shortcuts for practice actions instead of hard-coded keys.
- [x] Implement Half Time (HT) and Double Time (DT) on the shared speed controller.
- [x] Add Nightcore (NC) after DT timing and audio synchronisation are stable.
- [x] Log playback rate, loop transitions, seeks and routed-track discrepancies in Debug mode.
- [x] Verify Transpose, VOX, OCT and all latency settings in combination with speed and looping.
- [x] Verify repeated loops and long practice sessions do not accumulate drift or frame-time regressions.

### 0.5.0 - Scoring and feedback

- [x] Accumulate deterministic per-note scores from pitch similarity, voiced duration and confidence.
- [x] Classify accurate, high, low and missed singing without double-counting after seeks or loops.
- [x] Display live score, accuracy and consecutive-hit feedback in the gameplay HUD.
- [x] Apply Transpose and OCT consistently to live scoring and recorded score data.
- [x] Keep scoring stable across pause, seek, playback-rate and A-B loop transitions.
- [x] Write completed performances into lazer's native score and results flow.
- [x] Show an overall grade plus per-phrase accuracy, pitch bias, stability and missed sections.
- [x] Add automatic vocal-range detection and recommend a Transpose value before play.
- [x] Add note-driven health after it can be driven by the completed pitch-scoring pipeline.
- [x] Add deterministic scoring tests using recorded Pitch frames and fixed gameplay timestamps.
- [x] Verify scoring combinations for Transpose, OCT, HT, DT, latency and phrase looping.

### 0.5.1 - Scoring follow-up

- [ ] Show an overall grade plus per-phrase accuracy, pitch bias, stability and missed sections.
- [ ] Add automatic vocal-range detection and recommend a Transpose value before play.
- [ ] Verify scoring combinations for Transpose, OCT, HT, DT, latency and phrase looping in a single matrix.

### 0.6.0 - Recording and comparison

- [x] Record the microphone signal after input gain and before monitor routing.
- [x] Timestamp recorded audio against the gameplay clock and calibrated microphone latency.
- [x] Use a bounded background writer so disk activity cannot block microphone capture or gameplay.
- [x] Start, pause, seek and stop recording together with gameplay and practice loops.
- [x] Play back a recorded take alone or mixed with BGM and packaged original vocals.
- [x] Add per-phrase take recording, retry, selection and deletion.
- [x] Add an A-B comparison between the player's take and the packaged original vocal track.
- [x] Show recorded takes and comparison controls on the results screen.
- [x] Export complete performances and selected phrases as standard WAV files.
- [x] Store recording metadata needed to reproduce rate, transpose, latency and route settings.
- [x] Provide explicit recording state, storage location and cleanup controls.
- [ ] Verify long recordings, repeated retries and device changes do not leak streams or lose samples.

### 0.7.0 - Mode controls and long-session stability

- [x] Move microphone recording to an explicit `Recording` (`REC`) MOD.
- [x] Rename `Challenge` to `评分模式` (`SC`) and make it the only scoring switch.
- [x] Keep ordinary karaoke play judgement-free and score-free unless `评分模式` is selected.
- [x] Bound realtime note scoring to the note-local pitch-frame window.
- [x] Cache the final whole-performance score used by archive and phrase analysis.
- [x] Add long-song regression coverage for progressively increasing scoring work.

### 0.6.2 - Scoring pipeline fixes and known issues

- [x] Make scoring active by default; replace the former `评分模式` (`SC`) MOD
      with `Relax` (`RX`), which opts back out into unscored practice.
- [x] Fix `UtaGameplayScoringController` advancing its watermark from the raw
      "now" timestamp instead of the same capture-latency-adjusted time used by
      microphone frames, which rejected nearly every frame as "late".
- [x] Fix `DrawableUtaHitObject.UpdateHitStateTransforms` expiring notes on the
      framework's initial `ArmedState.Idle` setup call (not just on a real
      Hit/Miss), which killed objects before their async judgement could ever
      arrive and left the results screen stuck (`ScoreProcessor.HasCompleted`
      never true).
- [x] Fix `UtaRecordingRuntime` losing an in-progress take to `staging/` with
      no archive when gameplay exits before the natural-end watcher runs.
- [x] Add a Score HUD (`S` to toggle) with a configurable screen-corner
      position, and show the total score as a 0-100 scale on the HUD and
      results screen.
- [x] Add explicit `No Fail` (`NF`) MOD.
- [x] Move `Auto` (`AT`) to the `Fun` MOD category.
- [ ] **Known issue**: pressing `S` to hide the Score HUD sometimes leaves it
      permanently hidden - a second `S` press does not bring it back. Root
      cause not yet found; the gameplay-clock-seek debounce bug already ruled
      out. Needs a fresh log-driven pass in 0.6.3.

### 0.8.0 - Mobile remote control

- [ ] Host an optional local-network control service without requiring a cloud account.
- [ ] Pair a phone through a QR code containing a short-lived, single-use session credential.
- [ ] Require an explicit desktop action before accepting a new controller.
- [ ] Restrict remote actions to the current uta! session and revoke them when gameplay exits.
- [ ] Add reconnect, manual disconnect and revoke-all-controller controls.
- [ ] Reject unpaired clients, cross-origin requests, replayed credentials and excessive commands.
- [ ] Show the active network interface and pairing status before exposing the service.
- [ ] Build a responsive mobile interface for play, pause, seek, speed, A-B loops and phrase navigation.
- [ ] Add remote mixer, Transpose, VOX, OCT and latency controls with the same bounds as the desktop UI.
- [ ] Stream current lyrics, phrase position, detected pitch and live score to the phone.
- [ ] Keep desktop and mobile state synchronised after reconnects and rapid control changes.
- [ ] Provide a read-only spectator mode that cannot change playback or settings.
- [ ] Document the local-network security and privacy model.
- [ ] Test pairing, revocation, reconnects and malformed commands without replacing direct runtime tests with preflight checks.

### 0.9.0 - Skins, video and interface polish

- [ ] Support lazer-native ruleset skin lookups instead of introducing a separate skin package system.
- [ ] Expose skinable target notes, live Pitch curves, playhead, grid, lyrics and scoring feedback.
- [ ] Define safe fallbacks when a skin omits uta!-specific elements or fonts.
- [ ] Allow skins to customise colours, line weights, note shapes, spacing and animation intensity.
- [ ] Preserve colour-blind readability and contrast when applying custom skin colours.
- [ ] Add ruleset-aware video visibility, dimming, blur, offset and playback controls.
- [ ] Keep video synchronised through speed changes, seeks, loops and pauses.
- [ ] Add optional singing and scoring particles with a reduced-motion mode.
- [ ] Finish the native two-level settings navigation and remove remaining button/control inconsistencies.
- [ ] Add search terms, tooltips, reset behaviour and disabled-state explanations to every setting.
- [ ] Improve narrow-window, touch, keyboard and controller navigation.
- [ ] Move user-facing strings to localisation resources and provide English, Japanese and Chinese coverage.
- [ ] Add an import diagnostics view for invalid `.utz` packages without exposing internal stack traces.
- [ ] Implement Auto play using reference Pitch data for demonstrations and scoring regression tests.

### 0.10.0 - Optimisation and release hardening

- [ ] Consolidate playback, seek, rate and latency ownership into one documented clock architecture.
- [ ] Remove duplicate settings and audio state paths left by the 0.3-0.8 feature work.
- [ ] Profile update, draw, audio, microphone, recording and remote-control workloads with direct measurements.
- [ ] Add selectable low, balanced and high analysis-quality presets while retaining manual sampling control.
- [ ] Keep microphone and recording queues bounded under sustained CPU or storage pressure.
- [ ] Add device hot-plug recovery for microphone, monitor, BGM and vocal routes.
- [ ] Recover cleanly when an audio route, video decoder, recording target or remote service fails.
- [ ] Verify long sessions, repeated mode changes and thousands of seek/loop operations do not accumulate resources.
- [ ] Add configuration migrations for every renamed or type-changed setting since 0.3.0.
- [ ] Define compatibility behaviour for older `uta.song 0.1.x` packages and unsupported future schemas.
- [ ] Expand deterministic tests for import, clocks, scoring, recording, MOD combinations and remote commands.
- [ ] Run the supported desktop-platform build matrix or explicitly document any platform limitation before 1.0.
- [ ] Keep Nix osu! as the local API source of truth and maintain a reproducible CI fallback.
- [ ] Document performance measurements and remaining platform-specific limitations.

### 1.0.0 - Stable release

- [ ] Freeze the documented `.utz` playback, configuration, score and recording metadata compatibility promises.
- [ ] Verify upgrades preserve settings and imported songs from every supported pre-1.0 release.
- [ ] Complete end-to-end install, upgrade, troubleshooting, recording-privacy and mobile-pairing documentation.
- [ ] Complete keyboard, controller, touch, colour-blind and reduced-motion accessibility passes.
- [ ] Verify every supported MOD combination has deterministic timing and scoring behaviour.
- [ ] Verify BGM, VOX, lyrics, video, Pitch, scoring and recordings remain synchronised after rate and seek changes.
- [ ] Verify microphone and audio devices can be changed or disconnected without restarting osu!.
- [ ] Verify recordings cannot be started invisibly and can be located, exported and deleted by the user.
- [ ] Verify remote control is disabled by default and leaves no active listener after it is turned off.
- [ ] Complete a long-session soak run covering gameplay, practice, scoring, recording and remote control.
- [ ] Resolve all known crash, data-loss, unsafe-network-access and persistent desynchronisation defects.
- [ ] Build and verify a portable ruleset DLL against official NuGet API references without depending on Nix store paths.
- [ ] Publish versioned installation assets and release notes for the supported platform matrix.

After 1.0, additional song formats, online services, editor features and experimental MODs should be planned independently rather than expanding the stable playback contract implicitly.

## Scope

- osu!lazer compatibility target: Nix-installed `2026.804.2`.
- Accepted package contract: `uta.song` format `0.1.x` with `uta.pitch` scoring schema version 1.

## Acknowledgements

uta! is based on and has adapted substantial work from the original
[karaoke ruleset](https://github.com/karaoke-dev/karaoke). We thank its authors
and contributors for making their work available to the osu! community.

The real-time Pitch detector is adapted from open-source Pitch-analysis work
and uses a normalised-autocorrelation approach. We thank the developers and
researchers who made these techniques and their implementations openly
available.

uta! is built on [osu!](https://github.com/ppy/osu) and osu!lazer's ruleset and
framework APIs. We thank ppy and every osu! contributor for the game, framework
and tooling that make this project possible.

Licensed under GPL-3.0; see [LICENSE](LICENSE).
