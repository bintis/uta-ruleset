# uta!

`uta!` is a small osu!lazer ruleset for playing Uta Studio `.utz` song
packages. It deliberately contains only the playback path:

- `.utz` validation and import through lazer's public file-import API;
- word-timed lyrics with progressive highlighting;
- target notes, live microphone pitch and pitch feedback;
- Linux microphone support through the BASS runtime already shipped by lazer.

There is no editor, online layer, custom skin system, bundled icon pack, font,
or standalone media stack. Audio, video, artwork, song select, pausing, volume,
and results remain owned by osu!lazer.

## Build and test

Requires .NET 8:

```sh
dotnet build osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj -c Release
dotnet test osu.Game.Rulesets.Uta.Tests/osu.Game.Rulesets.Uta.Tests.csproj -c Release
```

Copy `osu.Game.Rulesets.Uta.dll` from `bin/Release/net8.0` into lazer's
`rulesets` directory. The host installation provides osu! and BASS assemblies;
the ruleset does not ship duplicate runtime libraries.

Select `uta!` once after launch to register native `.utz` drag-and-drop import.
Imported packages are validated in memory and handed to lazer's beatmap manager
as a standard archive, so lazer owns storage and media decoding.

## Scope

The accepted package contract is `uta.song` format `0.1.x` with the
`uta.pitch` scoring schema version 1. Package paths, sizes, hashes, transcript
timing, and note intervals are validated before import.

Licensed under GPL-3.0; see [LICENSE](LICENSE).
