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
  and live-microphone layers, fully integrated with the active lazer skin via
  the documented [uta! skin contract](Docs/SKINNING.md).
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

The consolidated build, automated-test, real-game debug and delivery procedure is
in [Docs/TESTING.md](Docs/TESTING.md). Installation, upgrades, troubleshooting,
recording privacy and phone pairing are covered in [Docs/USER-GUIDE.md](Docs/USER-GUIDE.md).

GitHub Releases publish an installable `uta-ruleset-v*.zip` with
`osu.Game.Rulesets.Uta.dll`, `libbassflac.so` and `BASSFLAC.txt`. Copy those
three files into lazer's `rulesets` directory.

To build from source, use .NET 8 and the locally installed Nix osu! package.
The Nix installation is the source of truth for osu! API and dependency
versions; the currently detected target is osu! `2026.726.0`. Resolve the
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

Completed work is recorded in [CHANGELOG.md](Docs/CHANGELOG.md); test and
acceptance procedures live in [TESTING.md](Docs/TESTING.md). Only current open
work is listed here.

### Runtime hardening

- [x] Verify the complete scoring matrix for Transpose, OCT, NC/DC, latency and loops.
- [x] Soak-test long recordings, repeated retries, device changes and storage-write failure.
- [x] Add device hot-plug recovery for microphone, monitor, BGM and vocal routes (with fallback to the lazer default output/input and automatic reconnect restoration).
- [x] Consolidate playback, seek, rate and latency ownership into the documented [single clock architecture](Docs/CLOCK-ARCHITECTURE.md).
- [x] Profile audio, microphone, scoring, recording and remote-control hot paths with bounded-buffer regression workloads.
- [x] Verify long sessions and repeated mode changes do not leak streams, listeners or buffers in deterministic runtime workloads.

### Interface and media

- [ ] Bind video visibility, dim, blur and offset to the supported lazer video drawable.
- [ ] Perform real-runtime verification that native video-event synchronisation survives pause, seek, loop and rate changes (the converter regression verifies that the native video event is emitted).
- [x] Finish keyboard, controller, touch, narrow-window, colour-blind and reduced-motion passes; see [ACCESSIBILITY.md](Docs/ACCESSIBILITY.md).
- [ ] Complete English, Japanese and Chinese localisation of desktop strings.
- [ ] Add consistent search terms, tooltips, reset actions and disabled-state explanations.

### Release readiness

- [x] Add migrations for renamed or type-changed pre-1.0 settings (including removal of the malformed legacy remote-port values written by affected builds).
- [x] Expand deterministic import, clock, scoring, recording and MOD-combination tests.
- [x] Run and document the supported desktop-platform build matrix.
- [x] Complete install, upgrade, troubleshooting, recording-privacy and pairing documentation in the [user guide](Docs/USER-GUIDE.md).
- [ ] Freeze the `.utz`, score, configuration and recording compatibility contracts for 1.0.
- [ ] Publish verified versioned assets only after crash, data-loss, network and desync gates pass.

Additional song formats, online services and editor features remain outside the
stable playback contract until planned separately.

## Scope

- osu!lazer compatibility target: Nix-installed `2026.726.0`.
- Accepted package contract: `uta.song` format `0.3.x` with `vocal-chart/1`. Older and future package versions are rejected.

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
