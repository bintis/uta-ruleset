# uta! remote protocol v1

The browser loads one embedded HTML resource over HTTP and opens `/ws` on the same host. A first connection uses `?ticket=...`; a reconnect uses `?session=...&secret=...`. Tickets are single-use. The server returns `welcome` with a reconnect secret or `resumed` without rotating it.

The desktop overlay (`UtaRemoteControlOverlay`) renders the pairing URL both as text and as a QR code (`UtaRemoteQrDisplay`, backed by a vendored copy of Manuel Bleichenbacher's QR code generator in `Remote/QrCodeGenerator/`, see its files for MIT attribution). Scanning the code with a phone camera opens the URL directly; the ticket and role travel in the URL fragment, which `uta-remote.html` reads on load to connect immediately, so no manual typing or extra pairing step is required.

Every command is JSON:

```json
{"type":"command","sequence":17,"command":"speed","value":1.1}
```

Boolean commands use `enabled`. Sequence numbers must increase within a session. A controller receives `ack` or `error`. The server broadcasts `state` around 10 times per second with song time/length, pause/rate, current and next lyrics, phrase position, detected MIDI pitch, similarity, voice activity, score, loop state, mixer state, Transpose, OCT/VOX and latency values.

Controller commands are: `play`, `pause`, `togglePlayback`, `seek`, `seekRelative`, `speed`, `setLoopA`, `setLoopB`, `clearLoop`, `previousPhrase`, `nextPhrase`, `retryPhrase`, `loopPhrase`, `bgmVolume`, `vocalsVolume`, `monitorVolume`, `transpose`, `octaveFold`, `originalVocals`, `microphoneLatency`, `accompanimentLatency`, `lyricsLatency`, `ping`, and `disconnect`.

Spectators may only send `ping` and `disconnect`. Numeric ranges are validated in `UtaRemoteProtocol` and then constrained again by the desktop bindables.
