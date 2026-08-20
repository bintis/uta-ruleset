# uta! remote protocol v1

The 0.8.1 phone client speaks a little-endian binary frame (`U` + version + kind + payload length + body) for commands, snapshots, queue, library results and UI traces. JSON remains accepted as a fallback. Snapshots at 10 Hz and 50-song library dumps are packed records, not tokenized JSON.

The browser loads one embedded HTML resource over HTTP and opens `/ws` on the same host. A first connection uses `?ticket=...`; a reconnect uses `?session=...&secret=...`. Tickets are single-use. The server returns `welcome` with a reconnect secret or `resumed` without rotating it.

The desktop overlay (`UtaRemoteControlOverlay`) renders the pairing URL both as text and as a QR code (`UtaRemoteQrDisplay`, backed by a vendored copy of Manuel Bleichenbacher's QR code generator in `Remote/QrCodeGenerator/`, see its files for MIT attribution). Scanning the code with a phone camera opens the URL directly; the ticket and role travel in the URL fragment, which `uta-remote.html` reads on load to connect immediately, so no manual typing or extra pairing step is required.

For debugging and compatibility, the server also accepts the equivalent JSON command form:

```json
{"type":"command","sequence":17,"command":"speed","value":1.1}
```

Boolean commands use `enabled`. Sequence numbers must increase within a session. A controller receives `ack` or `error`. The server broadcasts `state` around 10 times per second with song time/length, current song title/artist/difficulty, pause/rate, current and next lyrics, phrase position, detected MIDI pitch, similarity, voice activity, score, loop state, mixer state, Transpose, OCT/VOX, latency values, queue entries and next-song mods.

Controller commands are: `play`, `pause`, `togglePlayback`, `seek`, `seekRelative`, `speed`, `setLoopA`, `setLoopB`, `clearLoop`, `previousPhrase`, `nextPhrase`, `retryPhrase`, `loopPhrase`, `bgmVolume`, `vocalsVolume`, `monitorVolume`, `transpose`, `octaveFold`, `originalVocals`, `microphoneLatency`, `accompanimentLatency`, `lyricsLatency`, `ping`, `disconnect`, `librarySearch`, `queueAdd`, `queueAddNext`, `queueRemove`, `queueClear`, `queuePlayNow`, `skipCurrent`, `queueMove`, `queueMoveToTop`, `queueMoveToBottom`, `autoAdvance`, `setMod` and `queueConfigure`.

`queueAdd`, `queueAddNext` and `queueConfigure` accept an optional `options` object. Those values belong to the reservation, not the song that is already playing:

```json
{
  "type": "command",
  "sequence": 3,
  "command": "queueAdd",
  "text": "<beatmap guid>",
  "options": { "speed": 1.1, "transpose": 2, "mods": ["NF", "PR"] }
}
```

Speed is 0.5–1.5. Transpose is −6…+6. Mods must be remote-factory acronyms (`IQ`, `NF`, `RX`, `VOX`, `OCT`, `NPG`, `NL`, `AT`, `REC`, `PR`). Starting a reserved entry applies its options before gameplay is constructed; live Control-page key/speed still change only the current song.

Spectators may send `ping`, `disconnect`, `librarySearch` and `queueAdd` (including options). Numeric ranges are validated in `UtaRemoteProtocol` and then constrained again by the desktop bindables.

The phone client is a Rust WASM Canvas 2D remote with one native HTML search field. Pages are Library, Control (default), Queue and Info.
