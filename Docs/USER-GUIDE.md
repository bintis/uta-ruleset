# uta! user guide

## Install and upgrade

1. Download the release archive matching your platform support (currently Linux x64),
   or build using the command in the root [README](../README.md).
2. Exit osu!lazer before changing a ruleset DLL.
3. Copy `osu.Game.Rulesets.Uta.dll`, `libbassflac.so` and `BASSFLAC.txt` into
   lazer's `rulesets` directory. Keep the three files from the same release.
4. Start lazer and select `uta!` once. This registers `.utz` drag-and-drop
   import. Drop a package onto lazer and let its normal beatmap importer finish.

For an upgrade, replace all three files together. Imported songs and ruleset
settings are stored by lazer and must not be removed from its data directory.
If a newly installed DLL fails to load, restore the previous complete three-file
set, start lazer once, then inspect its runtime log before retrying the upgrade.

## Playing and troubleshooting

Choose BGM, original-vocal and microphone-monitor routes in uta! settings. The
empty output selection means **lazer default**. The selected microphone is used
for pitch detection; headphones are recommended when ear monitoring is enabled.

During gameplay, `O` opens quick settings, `S` toggles the score HUD, `P`
toggles the Practice HUD, `F8` opens the queue, `N` starts the next queued song,
and `K` opens the phone remote. The complete keyboard and real-game diagnostic
procedure is in [`TESTING.md`](TESTING.md).

If there is no microphone pitch:

- confirm lazer has OS microphone permission and select the intended input;
- check the input-level diagnostic and lower input gain if it clips;
- use **Auto-measure microphone latency** in uta! settings;
- use a distinct monitor output rather than selecting the microphone capture
  device as output;
- enable **Debug performance logging** and inspect `Uta debug microphone` and
  `Uta debug audio` lines in the latest lazer runtime log.

If a named BGM/vocal/monitor device is disconnected while playing, uta! falls
back to lazer's default route and restores the selected route after the device
returns. The saved choice is never silently replaced.

## Recording privacy and storage

Microphone recording is off unless the per-play `REC` mod is selected. While it
is active, the recording HUD is visible. Recordings are written locally under
the configured performance root; no recording, pitch replay or song data is
uploaded by uta!. Remove a take or its performance directory to delete its
local audio. See [`PERFORMANCE_ARCHIVE.md`](PERFORMANCE_ARCHIVE.md) for the
file layout, atomic-write behaviour and metadata.

## Phone remote pairing

The remote is disabled by default. Press `K` on the desktop and choose a
controller or spectator QR code. Scan it only from a phone on the same trusted
private network. Pairing tickets expire after 90 seconds and can be used once.
Use **Disconnect all clients** in the desktop overlay to revoke every connected
phone; leaving gameplay also stops the listener and clears all credentials.

The remote uses local HTTP/WebSocket rather than TLS. Never expose its port
through router forwarding, a public reverse proxy or untrusted Wi-Fi. Windows
may require a URL ACL for a LAN listener; the desktop overlay reports that bind
failure. The full trust model is in [`REMOTE-SECURITY.md`](REMOTE-SECURITY.md).
