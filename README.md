# uta-ruleset

`uta-ruleset` is the osu!lazer implementation of the Uta karaoke ecosystem. It is a hard fork of
[`karaoke-dev/karaoke`](https://github.com/karaoke-dev/karaoke), focused on singing packaged `.utz`
songs rather than preserving the original ruleset's editor and legacy DLL interfaces.

Version `0.1.0` is the first playable preview.

## Uta family

The projects share the same song package and singing model, but target different parts of the workflow:

```text
uta-studio ──exports──> .utz <──specified/tooling── utz
                          │
                          ├──played directly──> uta
                          │                    Independent, simplified Web/desktop player
                          │
                          └──imported into────> uta-ruleset
                                               Full osu!lazer ruleset integration
```

- **[uta-studio](https://github.com/bintis/uta-studio)** creates and analyses songs, lyrics, pitch notes,
  stems, artwork and video, then exports them as `.utz` packages.
- **[utz](https://github.com/bintis/utz)** owns the portable package format and related tooling. `.utz`
  is the stable boundary between authoring tools and players.
- **[uta](https://github.com/bintis/uta)** is the independent simplified Uta player. It does not require
  osu!lazer and is intended to provide a focused Web/desktop karaoke experience.
- **uta-ruleset** imports the same `.utz` packages into osu!lazer and reuses lazer's song select, MOD
  selector, settings, volume HUD, pause flow and results infrastructure.

`uta` and `uta-ruleset` are sibling players. Neither replaces the other, and both consume packages
produced by `uta-studio` using the `utz` format.

## Version 0.1.0

- Drag-and-drop `.utz` import through lazer's native import flow.
- Packaged artwork, video backgrounds, instrumental audio and optional guide/original vocal stems.
- FLAC playback through an optional native BASSFLAC plugin.
- Low-latency microphone capture, independent input gain and microphone monitoring on Linux.
- Uta's live pitch detection, flowing full-width pitch guide and sung-pitch trace.
- Word-timed lyrics with readings and progressive highlighting.
- Uta-derived pitch scoring presented as a compact `0–1000` score and `S/A/B/C/D` rank.
- Native lazer gap skipping for long intros, breaks and outros.
- Karaoke MODs for guide vocals, hiding the pitch guide, hiding lyrics and enabling failure.
- Native lazer visual settings and remapped volume HUD for **My Voice**, **BGM** and
  **Original Vocals**.
- Karaoke-only song filtering while this ruleset is selected.

## Build

The project targets .NET 8 and the current osu!lazer ruleset API:

```sh
dotnet build osu.Game.Rulesets.Karaoke/osu.Game.Rulesets.Karaoke.csproj -c Release
```

Copy `osu.Game.Rulesets.Karaoke.dll` and the contents of the generated `DLLs` directory into your
osu!lazer `rulesets` directory.

Some Linux/Nix osu! packages omit the FLAC decoder. For `.utz` songs containing FLAC, place the
official x86-64 `libbassflac.so` from [BASSFLAC](https://www.un4seen.com/bass.html#addons) beside the
ruleset DLL. The ruleset detects and registers it at startup.

## Credits

This project would not exist without **Andy/andy840119 and all contributors to
[`karaoke-dev/karaoke`](https://github.com/karaoke-dev/karaoke)**. Their osu!lazer ruleset,
karaoke object model, editor work and years of infrastructure form the foundation of this hard fork.

Thanks also to:

- [ppy/osu](https://github.com/ppy/osu) and [ppy/osu-framework](https://github.com/ppy/osu-framework)
  for lazer and its ruleset APIs.
- The Uta project family for the `.utz` format, lyric timing, microphone pitch detection and scoring
  model brought into this ruleset.
- [Un4seen Developments](https://www.un4seen.com/) for BASS and BASSFLAC.

## License

The ruleset is distributed under the [GNU GPL v3](LICENSE), following the original project. Embedded
components retain their respective copyright notices.
