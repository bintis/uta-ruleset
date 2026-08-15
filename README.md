# uta!

`uta!` is a small osu!lazer ruleset for playing Uta Studio `.utz` song
packages. It deliberately contains only the playback path:

- `.utz` validation and import through lazer's public file-import API;
- word-timed lyrics with progressive highlighting;
- target notes, live microphone pitch and pitch feedback;
- Linux microphone support through the BASS runtime already shipped by lazer;
- optional lyrics, pitch-guide and original-vocals mods;
- a `P` quick-settings panel and independent BGM, vocal and microphone-monitor
  output routing, with one shared mix bus per selected hardware device.
- lazer-native skip prompts for long intro, inter-phrase and trailing gaps;
- lazer's volume overlay remapped to microphone monitor, BGM and original vocals
  while playing;
- configurable lyric placement, typeface and scale, plus native score placement;
- a colour-blind-friendly dark pitch guide with distinct target and live-voice
  visual layers.

There is no editor, online layer, custom skin system, bundled icon pack, font,
or standalone media stack. Video, artwork, song select, the gameplay clock and
results remain owned by osu!lazer; Uta only owns its three gameplay audio routes.

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

### Singing and practice

- playback speed control;
- A-B section looping, per-phrase retry and quick navigation;
- automatic vocal-range detection with recommended transposition;
- voice recording, playback, original-vocal comparison and export;
- live gameplay scoring;
- post-song grading, rating and detailed feedback;
- selectable performance-quality levels and live diagnostics.

### osu! mods

- Double Time (DT) and Nightcore (NC);
- Half Time (HT);
- Auto play;
- a Fail mode where pitch-scoring mistakes drain health and the performance fails at zero health.

### Controls and presentation

- securely paired QR-code-based mobile web controls;
- ruleset-aware video playback controls;
- selectable particle effects;
- interface and control improvements.

## Scope

The accepted package contract is `uta.song` format `0.1.x` with the
`uta.pitch` scoring schema version 1. Package paths, sizes, hashes, transcript
timing, and note intervals are validated before import.

Licensed under GPL-3.0; see [LICENSE](LICENSE).
