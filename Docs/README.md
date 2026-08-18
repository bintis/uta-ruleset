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
- Scoring is active by default; `Relax` (`RX`) opts back out into unscored
  practice. `Recording` (`REC`) is enabled per play rather than by a
  persistent settings checkbox, and `Practice` (`PR`) gates a standalone
  Practice HUD with loop points, phrase navigation and a live speed control.
- Optional local-network mobile remote (`K`): QR pairing, controller and
  spectator roles, reconnect and revoke-all, with a single-file HTML client.
- Global song queue (`F8`) with search, add and reorder, next-song skip
  (`N`), and an `Immersive Queue` (`IQ`) MOD that continues after results.
- Bounded, path-stripped import diagnostics for invalid `.utz` packages.
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

### 0.4.0 - Playback and practice control ✅ complete

Pitch-preserving speed control, A-B loop points, phrase navigation, a native
Practice HUD group and HT/DT/NC playback variants, all kept synchronised with
BGM, VOX, lyrics and microphone latency. Full item list in
[CHANGELOG.md](CHANGELOG.md#040---2026-08-16).

### 0.5.0 - Scoring and feedback ✅ complete

Deterministic per-note scoring, live score/accuracy HUD feedback, overall
grade and per-phrase breakdown, automatic vocal-range/Transpose
recommendation, note-driven health and scoring tests. Full item list in
[CHANGELOG.md](CHANGELOG.md#050---2026-08-17).

### 0.5.1 - Scoring follow-up

- [x] Show an overall grade plus per-phrase accuracy, pitch bias, stability
      and missed sections (completed under 0.5.0).
- [x] Add automatic vocal-range detection and recommend a Transpose value
      before play (completed under 0.5.0).
- [ ] Verify scoring combinations for Transpose, OCT, HT, DT, latency and
      phrase looping in a single matrix.

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

### 0.6.2 - Scoring pipeline fixes and known issues ✅ complete

Made scoring active by default (`Relax` replaces the old opt-in `Scoring
Mode` switch), fixed the watermark/latency bug that rejected almost every
microphone frame, fixed notes expiring before their async judgement could
land (which silently broke the results screen), fixed an orphaned in-progress
recording take, and added the toggleable Score HUD. Full item list in
[CHANGELOG.md](CHANGELOG.md#062---2026-08-17). The HUD-sticks-hidden issue
noted at the time is fixed in 0.7.2 below.

### 0.7.0 - Mode controls and long-session stability ✅ complete

Moved recording behind an explicit `Recording` MOD, made the former
`Scoring Mode` MOD the sole scoring switch, bounded realtime note scoring to
a local pitch-frame window and cached whole-performance scoring for archive
and phrase analysis. Full item list in
[CHANGELOG.md](CHANGELOG.md#070---2026-08-17).

### 0.7.2 - Practice HUD, in-game settings and HUD reliability ✅ complete

- [x] Fix the Score HUD (`S`) getting permanently stuck hidden.
- [x] Add a standalone Practice HUD (`P`), gated behind a new `Practice`
      (`PR`) MOD, independent of the full settings panel (now `O`).
- [x] Replace the 11 fixed-value speed MODs with one live, pitch-preserving
      speed slider in the Practice HUD (bound to lazer's own
      `UserPlaybackRate`), plus a reset button and a live speed readout.
- [x] Add Chinese/English/Japanese HUD text for the Score HUD and Practice
      HUD, following lazer's own language setting live.
- [x] Add "Microphone monitor output" and its volume slider to the in-game
      settings panel (previously only reachable from the separate global
      settings page, outside gameplay).
- [x] Replace native `VisualSettings`/`InputSettings` in the in-game settings
      panel with a background dim/blur group relevant to uta!.
- [x] Fix lazer's native volume HUD popping open and eating scroll-wheel
      input when the in-game settings panel opens.
- [x] Fix hardcoded Chinese MOD descriptions (`No Fail`, `Relax`) showing
      regardless of the player's selected language.
- [ ] **Known issue**: the microphone monitor output setting does not
      reliably survive to the next play session. It applies live and
      correctly re-routes audio immediately when changed, but can read back
      as unset the next time gameplay starts. An explicit `config.Save()` on
      the settings panel closing did not resolve it - needs a fresh
      log-driven pass to find what resets it (or confirm it is a lazer-side
      `RulesetConfigManager` persistence issue) in a future release.

### 0.8.0 - Mobile remote control ✅ complete

Optional local-network remote (`K`) with QR pairing, single-use tickets,
controller/spectator roles, reconnect and revoke-all. The phone loads one
embedded HTML page; the listener is off by default and dies with gameplay.
A global song queue (`F8` / `N` / `IQ`) ships in the same release. Protocol
and security notes live in [REMOTE-PROTOCOL.md](REMOTE-PROTOCOL.md) and
[REMOTE-SECURITY.md](REMOTE-SECURITY.md). Full item list in
[CHANGELOG.md](CHANGELOG.md#080---2026-08-18). Real-device LAN, firewall and
mobile-browser soak tests remain a later release gate.

### 0.9.0 - Skins, video and interface polish

- [x] Support lazer-native ruleset skin lookups instead of introducing a separate skin package system (started in 0.8.0).
- [ ] Replace every existing hard-coded pitch/lyrics primitive with those skinable lookups (grid, notes, curves, playhead, lyrics, scoring feedback).
- [x] Define safe fallbacks when a skin omits uta!-specific elements or fonts (started in 0.8.0).
- [x] Allow skins to customise colours, line weights, note shapes, spacing and animation intensity (configuration lookups in 0.8.0).
- [x] Preserve colour-blind readability and contrast when applying custom skin colours (started in 0.8.0).
- [x] Add ruleset-aware video visibility, dimming, blur and offset settings (started in 0.8.0).
- [ ] Bind those video settings to the exact target lazer background/video drawable.
- [ ] Keep video synchronised through speed changes, seeks, loops and pauses once the ruleset binding exists (imported packages already inherit lazer's native video clock).
- [ ] Add optional singing and scoring particles; reduced-motion and intensity settings already exist.
- [ ] Finish the native two-level settings navigation and remove remaining button/control inconsistencies.
- [ ] Add search terms, tooltips, reset behaviour and disabled-state explanations to every setting.
- [ ] Improve narrow-window, touch, keyboard and controller navigation.
- [ ] Move remaining user-facing strings to localisation resources and provide English, Japanese and Chinese coverage (Score HUD, Practice HUD and the mobile remote already cover this; settings labels, tooltips and other desktop panels remain).
- [x] Add an import diagnostics view for invalid `.utz` packages without exposing internal stack traces (shipped in 0.8.0).
- [x] Implement Auto play using reference Pitch data for demonstrations and scoring regression tests (shipped in 0.8.0).

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

Desktop QR pairing uses a vendored copy of
[Manuel Bleichenbacher's QR code generator](https://github.com/manuelbl/QrCodeGenerator),
itself based on [Project Nayuki's QR code generator library](https://www.nayuki.io/page/qr-code-generator-library),
both MIT-licensed.

uta! is built on [osu!](https://github.com/ppy/osu) and osu!lazer's ruleset and
framework APIs. We thank ppy and every osu! contributor for the game, framework
and tooling that make this project possible.

Licensed under GPL-3.0; see [LICENSE](LICENSE).
