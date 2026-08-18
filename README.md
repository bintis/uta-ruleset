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

See [CHANGELOG.md](Docs/CHANGELOG.md) for completed work from 0.1.0 onward.

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
  spectator roles, reconnect and revoke-all, with a single-file WASM canvas
  client (Library / Control / Queue / Info).
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

GitHub Releases publish an installable `uta-ruleset-v*.zip` with
`osu.Game.Rulesets.Uta.dll`, `libbassflac.so` and `BASSFLAC.txt`. Copy those
three files into lazer's `rulesets` directory.

To build from source, use .NET 8 and the locally installed Nix osu! package.
The Nix installation is the source of truth for osu! API and dependency
versions; the currently detected target is osu! `2026.804.2`. Resolve the
active store path rather than committing a Nix store hash:

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

Finished items from 0.1.0 onward are in [CHANGELOG.md](Docs/CHANGELOG.md).
This list is only what is still open.

### 0.5.1 - Scoring follow-up

- [ ] Verify scoring combinations for Transpose, OCT, HT, DT, latency and
      phrase looping in a single matrix.

### 0.6.0 - Recording and comparison

- [ ] Verify long recordings, repeated retries and device changes do not leak streams or lose samples.

### 0.7.2 - Known issue

- [ ] The microphone monitor output setting does not reliably survive to the
      next play session. It applies live and correctly re-routes audio
      immediately when changed, but can read back as unset the next time
      gameplay starts. An explicit `config.Save()` on the settings panel
      closing did not resolve it - needs a fresh log-driven pass to find
      what resets it (or confirm it is a lazer-side `RulesetConfigManager`
      persistence issue).

### 0.9.0 - Skins, video and interface polish

- [ ] Replace every existing hard-coded pitch/lyrics primitive with those skinable lookups (grid, notes, curves, playhead, lyrics, scoring feedback).
- [ ] Bind those video settings to the exact target lazer background/video drawable.
- [ ] Keep video synchronised through speed changes, seeks, loops and pauses once the ruleset binding exists (imported packages already inherit lazer's native video clock).
- [ ] Add optional singing and scoring particles; reduced-motion and intensity settings already exist.
- [ ] Finish the native two-level settings navigation and remove remaining button/control inconsistencies.
- [ ] Add search terms, tooltips, reset behaviour and disabled-state explanations to every setting.
- [ ] Improve narrow-window, touch, keyboard and controller navigation.
- [ ] Move remaining user-facing strings to localisation resources and provide English, Japanese and Chinese coverage (Score HUD, Practice HUD and the mobile remote already cover this; settings labels, tooltips and other desktop panels remain).

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
