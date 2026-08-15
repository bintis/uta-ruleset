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

- [ ] Add pitch-preserving playback speed control from `0.50x` to `1.50x`.
- [ ] Keep the gameplay clock, BGM, VOX, lyrics and target pitch synchronised while changing speed.
- [ ] Keep microphone latency expressed in real milliseconds while playback speed changes.
- [ ] Add manual A and B loop points with clear/reset controls.
- [ ] Seek every routed audio source together when an A-B loop repeats.
- [ ] Break microphone pitch history cleanly at loop and seek boundaries.
- [ ] Derive phrase boundaries from transcript segments and target-note gaps.
- [ ] Add previous phrase, next phrase and retry-current-phrase actions.
- [ ] Add optional current-phrase looping with a `500-1000 ms` preparation lead-in.
- [ ] Put speed, loop and phrase navigation in a native `Practice` HUD group.
- [ ] Add configurable shortcuts for practice actions instead of hard-coded keys.
- [ ] Implement Half Time (HT) and Double Time (DT) on the shared speed controller.
- [ ] Add Nightcore (NC) after DT timing and audio synchronisation are stable.
- [ ] Log playback rate, loop transitions, seeks and routed-track discrepancies in Debug mode.
- [ ] Verify Transpose, VOX, OCT and all latency settings in combination with speed and looping.
- [ ] Verify repeated loops and long practice sessions do not accumulate drift or frame-time regressions.

### 0.5.0 - Scoring and feedback

- [ ] Accumulate deterministic per-note scores from pitch similarity, voiced duration and confidence.
- [ ] Classify accurate, high, low and missed singing without double-counting after seeks or loops.
- [ ] Display live score, accuracy and consecutive-hit feedback in the gameplay HUD.
- [ ] Apply Transpose and OCT consistently to live scoring and recorded score data.
- [ ] Keep scoring stable across pause, seek, playback-rate and A-B loop transitions.
- [ ] Write completed performances into lazer's native score and results flow.
- [ ] Show an overall grade plus per-phrase accuracy, pitch bias, stability and missed sections.
- [ ] Add automatic vocal-range detection and recommend a Transpose value before play.
- [ ] Add a Fail MOD after health drain can be driven by the completed pitch-scoring pipeline.
- [ ] Add deterministic scoring tests using recorded Pitch frames and fixed gameplay timestamps.
- [ ] Verify scoring combinations for Transpose, OCT, HT, DT, latency and phrase looping.

### 0.6.0 - Recording and comparison

- [ ] Record the microphone signal after input gain and before monitor routing.
- [ ] Timestamp recorded audio against the gameplay clock and calibrated microphone latency.
- [ ] Use a bounded background writer so disk activity cannot block microphone capture or gameplay.
- [ ] Start, pause, seek and stop recording together with gameplay and practice loops.
- [ ] Play back a recorded take alone or mixed with BGM and packaged original vocals.
- [ ] Add per-phrase take recording, retry, selection and deletion.
- [ ] Add an A-B comparison between the player's take and the packaged original vocal track.
- [ ] Show recorded takes and comparison controls on the results screen.
- [ ] Export complete performances and selected phrases as standard WAV files.
- [ ] Store recording metadata needed to reproduce rate, transpose, latency and route settings.
- [ ] Provide explicit recording state, storage location and cleanup controls.
- [ ] Verify long recordings, repeated retries and device changes do not leak streams or lose samples.

### 0.7.0 - Mobile remote control

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

### 0.8.0 - Skins, video and interface polish

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

### 0.9.0 - Optimisation and release hardening

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
