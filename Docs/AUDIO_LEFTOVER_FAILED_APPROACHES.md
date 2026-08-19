# Uta leftover BGM / VOX — failed approaches

人类留：电脑是Nixos，只有wayland，osu按F12可以截图  麦克风是AKG的 要求测试到进游戏，打开一个图，进去 有bgm 有耳返，退出 找另外的图 开原唱MOD 这时候 BGM，原唱，耳返 都有声音，退出，再选一个图，预览音乐要和选的图对上，你可以写osu的测试代码，也可以截图控制，但是不管怎么测试，记得看log 启动OSU的话 最好在1080P的那个显示器 （这个是240HZ的，好截图）而且 如果不在这个 显示器 agent进程可能会崩溃 切记

Date range: 2026-08-19. Machine: MARANTZ M4U output, AKG C44 capture, accompaniment latency **−411 ms**.

This file is a log-backed stop list. Do not re-try a section without a new log line that disproves it.

## What the player hears

1. Chart A instrumental keeps playing after ESC / next chart.
2. Original vocals (VOX) are missing or unusable on the next chart.

These are **two bugs**. Treating them as one mixer bug wasted most of the day.

## Hardware / graph that every session actually uses

From `Uta audio graph` / `Uta debug settings` (example `1787132475.runtime.log`):

| Path | Device | Why it exists |
|---|---|---|
| Clock + osu `TrackBass` | lazer default (same physical MARANTZ) | `WorkingBeatmap.Track` is the gameplay clock |
| Routed BGM | `MARANTZ M4U: USB Audio` via our BASS mixer | `NeedsRoutedBgm` is true whenever `\|latency\| ≥ 0.5 ms` |
| Native VOX track | same default device when vocals output == default | second osu `Track`, only if VOX is on |
| Ear monitor | MARANTZ push stream on the same mixer | microphone handler |

So **two BGM graphs share one speaker**: muted-or-unmuted TrackBass, plus a `MixerNonStop` instrumental. Stopping one does not stop the other.

## What the latest log actually proved (`1787132475`, DLL 18:27)

Clock stop on Asphodelos:

```
Uta halted leftover playback: streams=1 mixerChannels=0 busesStopped=1
GameplayClockContainer stopped via call to StopGameplayClock
OsuScreenStack exit from SoloPlayer
Song select decided to ensurePlayingSelected
Invalidating working beatmap cache for Rena - Asphodelos
MusicController starting playback to EnsurePlayingSomething
```

Then starting Snow Crystal:

```
Focus contention triggered by UserModSelectOverlay
Invalidating working beatmap cache for ... Snow Crystal
PlayerLoader entered
InvalidOperationException: Cannot access Track without first calling LoadTrack
  at WorkingBeatmap.get_Track()
  at PlayerLoader.OnEntering
```

Facts, not guesses:

- Our mixer **did** stop (`busesStopped=1`). Mixer leftover is not what the player still hears after this DLL.
- osu immediately **restarts chart A** on TrackBass (`ensurePlayingSelected` / `EnsurePlayingSomething`).
- VOX was **never in the play**: `mods: [] originalVocalsEnabled=False vocals=n/a`. Overlay opened; constructor still `[]`.
- Calling `Track.Stop()` ourselves, plus cache invalidation, left the next `PlayerLoader` with **no loaded track** → red screen.

## Two bugs, two log signatures

### A. Leftover last-song BGM

**Signature of mixer leftover (older DLLs):** every play logs `Uta output mixer ready: MARANTZ…` again; no `Uta halted`.

**Signature of TrackBass leftover (current DLL):** mixer halt log is present (`busesStopped=1`), then **the next line from osu** is `ensurePlayingSelected` / `MusicController starting playback`. That is chart A preview/gameplay track on the same device.

### B. VOX silent

**Signature:** `Uta debug ruleset mods: [] originalVocalsEnabled=False` and `vocals=n/a` / `applied: 0% (enabled=False slider=78%)`.

When VOX *is* in the play, logs look like `1787124041`: `mods: [VOX] originalVocalsEnabled=True` and `vocals=18610.1ms` locked to the clock. The vocal decoder is not the broken part.

Opening `UserModSelectOverlay` is **not** evidence that VOX entered `selectedMods`. Need the constructor line `[VOX]`.

Leftover BGM can *mask* VOX (same speaker, extra instrumental). It cannot explain `enabled=False`.

## Failed approaches (do not repeat)

### 1. Per-play `MixerNonStop` bus, stop it in `Dispose`

**Tried:** each `DrawableUtaRuleset` owned a mixer; `Dispose` `ChannelStop` + `StreamFree`.

**Why it failed:** `DrawableRuleset` dispose is **async**. Chart B’s mixer is already playing while chart A’s mixer is still alive. Log: `Uta output mixer ready` once per play.

**Do not retry** “just dispose harder” on the async path.

### 2. Static one-bus-per-device, no halt

**Tried:** cache mixer handles process-wide so we stop creating a new bus every play.

**Why it failed:** `MixerNonStop` + leftover **channels** still play on the cached bus. Static cache without a halt just changes *which* mixer leaks.

### 3. Halt only `UtaRoutedAudioStream` objects we still have in a list

**Tried:** `HaltAll()` on a static `live` list at next `load`.

**Why it failed:** async dispose already `Remove`s streams from `live` (and may `StreamFree` on the wrong BASS device). Next load logs `streams=0 mixerChannels=0` while the player still hears chart A. Empty list ≠ silent hardware.

### 4. `MixerGetChannels` drain of non-monitor sources

**Tried:** walk mixer channels, skip ear-monitor handles, `MixerRemoveChannel` + `StreamFree`.

**Why it failed:** log repeatedly `mixerChannels=0` while leftover remains. Either the API saw an empty mixer (leftover is **not** on that mixer) or drain ran after the tracked stream was already dropped. Either way it did not match what the player heard.

### 5. Halt routed streams on gameplay clock pause (ESC)

**Tried:** `onGameplayPausedChanged` → `dropRoutedPlayback()` on the update thread.

**What it did:** this **does** run. `1787132475` shows halt **before** `StopGameplayClock`.

**Why leftover remains:** osu then calls `ensurePlayingSelected`. TrackBass of chart A starts again. Mixer work cannot cancel MusicController.

### 6. `ChannelStop` the mixer bus (`busesStopped=1`)

**Tried:** after draining, `Bass.ChannelStop(bus)`.

**What it did:** mixer output is actually stopped. Log: `busesStopped=1`.

**Why leftover remains:** TrackBass / MusicController is a **different** graph on the same speaker.

**Do not spend more time on mixer stop variants expecting that to kill last-song BGM after ESC.**

### 7. `DestroyBuses()` / `StreamFree` the mixer on next load

**Tried:** next play destroys cached mixers and builds a new one.

**Status:** may be fine for *mixer* hygiene. It is **not** the leftover-after-ESC path. Do not treat “new mixer every play” as the user-facing fix.

### 8. `WorkingBeatmap.Track.Stop()` / keep a `lastGameplayTrack` and stop it on next load

**Tried:** stop osu’s track ourselves so MusicController cannot keep chart A.

**Why it failed:** `1787132475` — next `PlayerLoader.OnEntering` throws `Cannot access Track without first calling LoadTrack`. We do not own track lifetime. Stop/cache-invalidate leaves the next screen with `TrackLoaded == false` and no `ensurePlayingSelected` in between.

**Reverted.** Do not `Stop()` / recycle / unload `WorkingBeatmap.Track` from Uta.

### 9. `RecordGetDeviceInfo(deviceIndex)` after mixer `CurrentDevice` changes

**Tried:** incidental; `Bass.DefaultDevice` is `-1`.

**Why it failed:** `1787130124` red screen `BassException: Error: Device` at `UtaMicrophoneHandler.start()`. `RecordInit(-1)` can succeed; throwing `RecordGetDeviceInfo(-1)` kills the game. Follow-up play logs `Already`.

**Fixed separately** (non-throwing info + try/catch). Unrelated to leftover BGM. Do not “fix leftover” by touching RecordInit again.

### 10. Guess the physical mic / MARANTZ capture

**Tried:** early mute/VOX debugging.

**Why it failed:** user already said capture worked on previous versions; later AKG C44 is the working input. Log `microphone: started deviceIndex=1 name='AKG C44…'`. Do not re-open device-set unless the start line disappears.

### 11. Volume overlay / ORIGINAL VOCALS slider as the VOX switch

**Tried:** player expectation; we gated vocals with `UtaModOriginalVocals` only (`只认MOD`).

**Why it looks broken:** slider stays ~78% while `applied: 0% (enabled=False)`. Overlay meter is **level**, not the gate. Need `[VOX]` in `Uta debug ruleset mods`.

### 12. Bind game-wide `SelectedMods` via reflection so VOX clicked during PlayerLoader applies

**Tried:** `OsuGameBase.SelectedMods` is not public; reflection + OR with constructor mods.

**Status:** unproven. Latest leftover sessions still enter with `mods: []` after `UserModSelectOverlay`. Until a log shows `Uta original vocals on constructor=[] live=[] game=[VOX]`, do not keep stacking VOX bind hacks.

### 13. `Player.Restart` for F7 / next chart

**Tried:** earlier; leased `WorkingBeatmap` loaded the **previous** chart with the next song’s audio.

**Replacement:** `LeaveForQueuedChart` (Exit + song select). Different bug from leftover BGM. Do not go back to `Player.Restart`.

### 14. `WorkingBeatmap.Track.Stop()` on the **incoming** clock track

Same as §8. Stopping the chart you are about to play unloads `PlayerLoader`. Reverted. Stopping a *previous* loaded track is a different experiment (§16).

### 15. `MusicController.Stop(false)` only, at next play start

**Tried:** `beginFreshSession` → `ResetTrackAdjustments` + `Stop(false)` if `IsPlaying`.

**Log `1787133670`:** play 2/3 both logged `musicWasPlaying=True` then built a new mixer. Leftover remained.

**Why it failed:** by the time play 2 loads, MusicController has often **already switched** to chart B’s preview. `Stop(false)` silences B’s preview (then gameplay starts B). Chart A’s `WorkingBeatmap.Track` can still be `IsRunning` on a **different** Track object. Confirmed in §16.

### 16. Also `Stop()` LastPlayedBeatmap.Track if loaded, running, and id ≠ incoming

**Tried:** DLL 19:04. Log `1787133937` (19:07).

Play 1 Asphodelos (first play, expected clean):

```
Uta audio session reset: musicWasPlaying=True trackLoaded=True
Uta audio graph: chart='Rena - Asphodelos' … vocals=n/a
mods: [] originalVocalsEnabled=False
```

ESC:

```
Uta halted leftover playback: streams=1 mixerChannels=0 busesStopped=1
Song select decided to ensurePlayingSelected
Invalidating working beatmap cache for Rena - Asphodelos
```

Then carousel `Uta loaded preview track` for days / Fast Forward / Growing / … / エウテルペ while **LastPlayedBeatmap is still Asphodelos**.

Play 2 エウテルペ:

```
mods: [] originalVocalsEnabled=False
Uta halted leftover playback: streams=0 mixerChannels=0 busesStopped=1
Uta audio session reset: previous='Rena - Asphodelos' running=True current='EGOIST - エウテルペ'
Uta audio session reset: musicWasPlaying=False trackLoaded=True
Uta audio graph: chart='EGOIST - エウテルペ' … vocals=n/a
```

**Facts:**

- Mixer leftover is dead (`busesStopped=1`, `mixerChannels=0`).
- MusicController was **already not playing** (`musicWasPlaying=False`).
- Asphodelos’ TrackBass was **still running** after the player had previewed several other songs. That is the leftover: gameplay Track of chart A is not owned by MusicController.CurrentTrack anymore, so changing previews does not stop it.
- Overlay opened twice (`10:06:18`, `10:06:29`); constructor still `mods: []`. Slider 82%. VOX never entered the play.
- `Uta debug audio: bgm=…ms` only reports our routed stream. It **cannot** show leftover TrackBass. Do not use a matching clock/bgm drift line as proof leftover is gone.

**Why leftover can still be heard after this Stop:**

1. **Between songs (proven):** ESC → `ensurePlayingSelected` **restarts Asphodelos** before we ever Stop previous. That is leftover on song select, every time.
2. **Into play 2:** we Stop previous at `load`. If the player still hears Asphodelos *during* エウテルペ, `Track.Stop()` did not mute the BASS channel, or something started it again after `load` (clock start / lease). There is **no** post-Stop `IsRunning` log yet.
3. **Uta `LoadTrack` on every carousel move** (`Uta loaded preview track for '…'`) loads extra WorkingBeatmap tracks. It does not stop LastPlayedBeatmap. That is how Asphodelos stays `running=True` through six previews.

**Do not:** Stop the incoming chart’s Track. **Do not:** treat `musicWasPlaying=False` as “no leftover”.

## Osu source (the actual owner of TrackBass)

From `/home/bintis/Code/uta-project/osu`:

- `WorkingBeatmap.Track`: *“This generally happens via MusicController when changing the global beatmap.”* `LoadTrack()` **replaces** `track` without disposing the previous instance.
- `MusicController.changeTrack()`: fade out the old `DrawableTrack` and **Expire()** it, then `new DrawableTrack(current.LoadTrack())`. That is how osu stops the previous chart.
- `SongSelect.ensurePlayingSelected()`: `music.Play(isNewTrack)` so **exiting gameplay resumes the same track** and does not jump to the preview point.
- `PlayerLoader.OnSuspending` (entering Player): `Track.Stop()` **then** `RemoveAdjustment(Volume)` — *“stop the track before removing adjustment to avoid a volume spike.”*
- `MasterGameplayClockContainer`: `requireDecoupling: true`. Clock `Stop()` does not reliably stop `WorkingBeatmap.Track`. Log: clock stopped, `previous.Track.IsRunning` still true.

Uta violated this by `EnsurePreviewTrack` → `LoadTrack()` on every carousel change (`Uta loaded preview track for '…'`). Those TrackBass instances are not inside MusicController’s `DrawableTrack`, so `changeTrack()` never Expire()s them. LastPlayedBeatmap keeps the gameplay instance, still running.

## What is actually still legal

### 17. Stop calling `LoadTrack` from `EnsurePreviewTrack` (osu-source “MusicController owns Track”)

**Tried:** DLL 19:22. Logs `1787134969` (19:23) and `1787135020` (19:24).

**What worked:**

- Carousel no longer logs `Uta loaded preview track`. That extra TrackBass factory is gone.
- **VOX works when it is actually in mods.** `1787135020` play 1: `mods: [NF,VOX] originalVocalsEnabled=True`, `Uta vocals route ready … native=True`, `vocals=48816.3ms` locked to the clock. Leftover BGM was never why VOX was silent; `mods: []` was.

**What failed (`1787134969`):**

Play 1 Asphodelos OK (`mods: []`). ESC:

```
Uta halted leftover playback: streams=1 mixerChannels=0 busesStopped=1
Song select decided to ensurePlayingSelected
Invalidating working beatmap cache for Rena - Asphodelos
UserModSelectOverlay
Invalidating working beatmap cache for Rena - Asphodelos
Uta published song select beatmap to game-wide: Rena - Asphodelos
Invalidating working beatmap cache for Rena - Asphodelos
Song select decided to ensurePlayingSelected
PlayerLoader entered
InvalidOperationException: Cannot access Track without first calling LoadTrack
  at PlayerLoader.OnEntering  (Beatmap.Value.Track.AddAdjustment)
```

Re-entering the **same** chart after ESC: Uta publish + cache invalidation leaves `PlayerLoader.Beatmap.Value.TrackLoaded == false`. osu’s `PlayerLoader.OnEntering` **requires** a loaded track. MusicController may have a DrawableTrack, but that is not this WorkingBeatmap instance.

Same crash on back button: `PlayerLoader.OnExiting` also uses `Beatmap.Value.Track.RemoveAdjustment`.

**Why osu can skip Uta LoadTrack and we cannot:** standard osu beatmaps stay TrackLoaded on the same WorkingBeatmap MusicController already called `LoadTrack()` on. Uta’s `publishToGameWide` / cache invalidation produces (or resets) an instance PlayerLoader then reads. Removing carousel LoadTrack is correct for leftover; removing **all** LoadTrack including the instance about to enter PlayerLoader is not.

**Do not:** leave `EnsurePreviewTrack` as a total no-op. **Do not:** LoadTrack every carousel WorkingBeatmap without stopping the previous instance.

User (`1787134969` / `1787135020`): this version is **worse** than leftover — song-select preview **stays the first chart forever**, and the second PlayerLoader **crashes**. First-play-only correctness is gone.

Follow-up that was tried: `LoadTrack` current after invalidation + `StopLeftoverTrack` previous. Failed in §19.

### 19. LoadTrack current + Stop LastPlayedBeatmap / OldValue on carousel (`1787136052`)

**Tried:** DLL 19:33.

Play 1 月の雫 (first play, `mods: []`). ESC mixer halt. Carousel:

```
Uta loaded preview track for 'azusa - 告白'
Uta stopped leftover track '月の雫' for 'ヨワネハキ'
Uta loaded preview track for 'ヨワネハキ'
Uta stopped leftover track '月の雫' for 'シナリオ'
… same 月の雫 stop for エウテルペ / きらきらセレナーデ / Snow Crystal / days / Asphodelos …
Song select decided to ensurePlayingSelected   (after every one)
```

Play 2 Asphodelos:

```
previous='月の雫' running=True
musicWasPlaying=False
```

**Why it failed:**

1. **Stop always targets LastPlayedBeatmap (月の雫), not the previous carousel chart.** `OldValue` on the song-select bindable is still the leased gameplay chart. 告白 / ヨワネハキ / … tracks are loaded and never stopped.
2. **`Track.Stop()` on 月の雫 does not stick.** It is logged as stopped for every subsequent carousel item, so `IsRunning` became true again in between. `ensurePlayingSelected` → `music.Play()` restarts MusicController.CurrentTrack, which is still 月の雫 because **game-wide Beatmap is leased** (`Uta could not enable the game-wide beatmap bindable: leased` on earlier sessions). Preview you *hear* is the first/last played chart, not the highlighted carousel row.
3. **LoadTrack of the highlighted chart** only prepares PlayerLoader’s instance. It does not change MusicController.CurrentTrack. Two graphs again: MusicController = 月の雫, Uta LoadTrack = whatever you hovered.

Later plays: `previous.running=False` (gameplay Track.Stop on pause worked for *that* chart) but `musicWasPlaying=True` at next enter — MusicController still holding leftover preview.

No PlayerLoader crash this run (LoadTrack restored). Preview/leftover still first-song. `mods: []` entire file; overlay opened once.

**Do not:** Stop LastPlayedBeatmap on every carousel tick expecting that to switch preview. **Do not:** LoadTrack a private WorkingBeatmap and assume the speaker follows it. Preview follows **MusicController.CurrentTrack** = global Beatmap bindable. If that bindable stays leased to chart A, the user will always hear A.

Follow-up (DLL 19:52, log `1787136837`): return the gameplay beatmap lease on leave (`Player.Beatmap` as `LeasedBindable.Return()`, and again on `UtaGlobalExtension.Dispose`) so song select can write game-wide Beatmap and MusicController `changeTrack()`s. Carousel leftover stop uses **last previewed** WorkingBeatmap, not LastPlayedBeatmap. Failed: see §20.

### 20. Return lease on leave via `player.Beatmap is LeasedBindable` + carousel `LoadTrack` after `publishToGameWide` (`1787136837`)

**Tried:** DLL 19:52 (1039360 bytes). Session-bridge `Update` when `!player.IsCurrentScreen()`: `HaltIfSingleSession` then `if (player.Beatmap is LeasedBindable<WorkingBeatmap> leased) leased.Return()`. Carousel: `StopLeftoverTrack(lastPreviewBeatmap)` then `publishToGameWide` then `EnsurePreviewTrack` (LoadTrack if `!TrackLoaded`).

Play 1 Departures (first play, clean):

```
Uta audio session reset: musicWasPlaying=True trackLoaded=True
mods: [] originalVocalsEnabled=False vocals=n/a
Uta returned the gameplay beatmap lease after loading the session-local chart.   ← LoadComplete, not leave
Uta attached song select beatmap bindable disabled=False gameWide=disabled=False.
```

ESC:

```
Uta halted leftover playback: streams=1 mixerChannels=0 busesStopped=1
Song select decided to ensurePlayingSelected
Invalidating working beatmap cache for EGOIST - Departures
```

**No** `returned the beatmap lease on leaving gameplay`. `player.Beatmap` is not the original `LeasedBindable` (it is a `GetBoundCopy` with `source=null`).

Carousel after ESC (this is the leftover-preview signature):

```
Uta published song select beatmap to game-wide: じん feat.Lia - days
Uta loaded preview track for 'じん feat.Lia - days'
Invalidating working beatmap cache for じん feat.Lia - days
Song select decided to ensurePlayingSelected
```

Then AMATERRAS / Asphodelos / シナリオ / … / 夢想歌: publish + LoadTrack + cache invalidate, **no** `Game-wide working beatmap updated to …`, **no** `Uta stopped leftover track`, and `ensurePlayingSelected` only on the first hop (`days`).

Before the first play, native song select logs `Game-wide working beatmap updated` then cache invalidate then `ensurePlayingSelected`. After Uta gameplay that line is gone. `publishToGameWide` is writing a bindable MusicController is **not** watching.

Play 2 夢想歌:

```
previous='EGOIST - Departures' running=True
musicWasPlaying=False
Uta could not enable the game-wide beatmap bindable: currently in a leased state
Uta returned the gameplay beatmap lease after loading the session-local chart.   ← again at LoadComplete
```

**Facts:**

1. Mixer halt is still not what the speaker is playing. `busesStopped=1` then osu `ensurePlayingSelected` restarts MusicController.CurrentTrack (still Departures).
2. Lease return on leave never ran. The only successful `Return()` is `UtaGlobalExtension` at the **next** play’s LoadComplete, so song select spent the whole carousel interval with MusicController stuck on chart A.
3. `WorkingBeatmap.LoadTrack()` replaces `track` without disposing the previous BASS stream. Uta LoadTrack of the highlighted row, then `ensureGlobalBeatmapValid` → `GetWorkingBeatmap(..., refetch: true)` **drops that WorkingBeatmap from the cache**. MusicController never received that instance, so the new TrackBass is a leak; CurrentTrack stays Departures.
4. `previous.running=True` while `musicWasPlaying=False` means LastPlayedBeatmap.Track is **not** MusicController.CurrentTrack. Stopping / LoadTrack-ing a private WorkingBeatmap does not change the speaker.
5. Overlay opened; constructor still `mods: []`. VOX is still a separate bug (need `[VOX]`).

**Do not:** `player.Beatmap is LeasedBindable` as the leave-return path. **Do not:** `LoadTrack` every carousel WorkingBeatmap and assume preview follows it. **Do not:** treat `publishToGameWide` as `MusicController.changeBeatmap` unless the next log contains `Game-wide working beatmap updated to <the highlighted row>`.

### 21. Reflection `changeBeatmap` + `Play()` on leave restarts leftover (`1787139196`)

**Tried:** DLL 20:30 (1040896 bytes). Carousel/leave call `MusicController.changeBeatmap` via reflection, then `Play()`. Stored lease Return on leave. No Uta `LoadTrack` on carousel.

Play 1 Asphodelos clean. ESC:

```
Uta halted leftover playback: streams=1 mixerChannels=0 busesStopped=1
Song select decided to ensurePlayingSelected
Uta asked MusicController to change beatmap to 'Rena - Asphodelos' playing=True
Uta asked MusicController to change beatmap to 'Rena - Asphodelos' playing=True
```

We **restarted leftover ourselves.** `PrepareSongSelectPreview` on session dispose / `handleSessionChanged` syncs MusicController **back to LastPlayedBeatmap** and `Play()`s it. That is the same chart `ensurePlayingSelected` just resumed. Two Uta Plays after osu already resumed it.

Carousel (RAINBOW_GIRL → Snow Crystal → … → ハルモニア):

```
Uta asked MusicController to change beatmap to '…' playing=False
Uta published song select beatmap to game-wide: …
Invalidating working beatmap cache for …
```

Still **no** `Game-wide working beatmap updated`. `Play()` is `CurrentTrack.StartAsync()` so `IsPlaying` is false on the next line — preview of the highlighted row never starts on the update thread. `changeTrack()` only fades volume; it does **not** Start the new DrawableTrack. Native osu starts it later via `ensurePlayingSelected` → `music.Play(isNewTrack)`. That `ensurePlayingSelected` is skipped while leftover CurrentTrack is still running (`!track.IsRunning` is false). After `changeBeatmap` the new track is not running either, and `ensurePlayingSelected` already ran **before** our handler (SongSelect subscribed first).

Play 2 ハルモニア:

```
previous='Rena - Asphodelos' running=False stillRunning=False
musicWasPlaying=True
```

Gameplay Track of chart A is dead. MusicController is still playing (ハルモニア preview we `Play()`d on the PlayerLoader click, or leftover A if Expire did not take it). `music.Stop(false)` is `StopAsync` and `Stop(true)` **returns immediately** when `AllowTrackControl` is false (PlayerLoader).

Osu source (`MusicController.Stop`, `SongSelect.ensurePlayingSelected`, `changeTrack`):

- `changeTrack` Expire-fades the old DrawableTrack, installs a new one at volume 0, **does not Start**.
- `ensurePlayingSelected` is the Start: skipped if CurrentTrack is running, or if `UserPauseRequested && !isNewTrack`.
- `Stop(requestedByUser: true)` no-ops when `AllowTrackControl` is false (PlayerLoader on the way out).
- `MasterGameplayClockContainer` is `requireDecoupling: true` so clock Stop ≠ Track.Stop.

**Do not:** `PrepareSongSelectPreview` → `changeBeatmap(LastPlayed)` + `Play()` after ESC. **Do not:** `Play()`/`StopAsync` as the leftover kill — use synchronous `CurrentTrack.Stop()`. **Do not:** rely on `ensurePlayingSelected` to start the new preview if leftover CurrentTrack was still running when SongSelect’s handler ran.

### 22. Writing MusicController's bindable then `Start()` leftover CurrentTrack (`1787140285`)

**Tried:** DLL 20:50 (1042944 bytes). Pause/leave `CurrentTrack.Stop()` + `UserPauseRequested`. Carousel: set extracted `MusicController.beatmap` then `CurrentTrack.Start()`.

Play 1 月の雫. ESC: `stopped MusicController on leave wasRunning=False` — **no** `ensurePlayingSelected` (UserPauseRequested worked). Carousel:

```
Uta set MusicController beatmap bindable to 'H2O - 想い出がいっぱい'
Uta published song select beatmap to game-wide: H2O …
Uta started MusicController track 'H2O …' switched=True playing=True
```

Still **no** `Game-wide working beatmap updated`. `switched=True` is a lie: `trySetMusicBeatmap` wrote a bindable MusicController is **not** watching, so `tryChangeBeatmap` was **skipped**. `CurrentTrack.Start()` then starts whatever DrawableTrack was already installed — chart A, 月の雫. Every carousel row “plays” the leftover first song.

Play 2 赤い糸: `previous running=False stillPlaying=False musicWasPlaying=True`. Mixer rebuilt. Leftover is the TrackBass we kept `Start()`ing on song select.

QL / F7 (`LeaveForQueuedChart` + `UtaPlaybackCoordinator.navigate`) does **not** do this. It:

1. `DestroyAllPlayback()` (StreamFree mixers, not just ChannelStop)
2. `Player.Exit()`
3. `IPerformFromScreenRunner` back to SongSelect
4. `IHandlePresentBeatmap.PresentBeatmap(working, ruleset)` — sets **SongSelect.Beatmap**, which is what osu MusicController actually follows when the lease is gone
5. `GetForwardActions` play

**Do not:** treat a successful write to `MusicController.beatmap` as `changeBeatmap`. **Do not:** `CurrentTrack.Start()` unless `changeBeatmap` ran for a **different** chart than LastPlayed. Match F7: DestroyAllPlayback on leave; always `changeBeatmap` (or PresentBeatmap) for a new chart.

### 23. F7-style carousel hijack on a dead SongSelect (`1787141051`)

**Tried:** DLL 20:58 (1043456 bytes). DestroyAllPlayback on leave, always `changeBeatmap`, BindTo SongSelect→game-wide, Start only if LastPlayed id differs.

Play 1 もしも命が描けたら. ESC:

```
Uta could not rebind song select beatmap: An already bound bindable cannot be bound again.
Uta halted leftover playback: busesStopped=1
Uta stopped MusicController on leave wasRunning=False stillRunning=False
```

Then the user **left song select to MainMenu** (`exit from SoloSongSelect#495`). osu:

```
MusicController starting playback to EnsurePlayingSomething
```

`UserPauseRequested` did **not** stick (`EnsurePlayingSomething` returns immediately if it is true). Reflection set on the public property was a no-op. Main menu restarted leftover chart A.

Then a **new** `SoloSongSelect#542` was pushed. Uta was still watching `#495.Beatmap` (disposed). **Zero** `Uta asked MusicController to change beatmap` / `set MusicController beatmap bindable` on the new carousel. Cache invalidation only. No `Game-wide working beatmap updated`. PlayerLoader:

```
InvalidOperationException: Cannot access Track without first calling LoadTrack
  at PlayerLoader.OnEntering
```

Same red screen as §17. `ensureGlobalBeatmapValid` refetch-invalidates the highlighted WorkingBeatmap; MusicController never LoadTrack'd that instance.

F7 works because it stays on the **same** SongSelect, PresentBeatmap writes **that** screen’s Beatmap after PlayerLoader has disposed the lease, then immediately plays. Hijacking a previous SongSelect bindable does nothing after MainMenu. Early `Return()` at LoadComplete is not what F7 does — F7 lets PlayerLoader dispose return the lease.

**Do not:** watch SongSelect.ValueChanged across screen instances. **Do not:** Return the lease at gameplay LoadComplete. **Do not:** BindTo a bindable that is already bound (it is bound to the lease copy, not game-wide). **Do not:** skip LoadTrack on the instance PlayerLoader will read.

### 18. Halt/pause `mainTrack.Stop()` copied from `PlayerLoader.OnSuspending`

Stacked with §17. Clock pause now Stops the gameplay Track. Combined with cache invalidation, the same WorkingBeatmap can be `TrackLoaded == false` by the time the next PlayerLoader enters. Recorded as part of the §17 red screen; do not Stop+invalidate without a following LoadTrack on that instance.

### 24. 切歌之后原唱没声音 (`1787146243`, `1787146753`)

Latest leftover-era sessions (DLL 21:24, logs 22:31 / 22:40):

```
Uta debug ruleset mods: [] originalVocalsEnabled=False
Uta vocals volume applied: 0% (enabled=False slider=100%)
Uta debug audio: … vocals=n/a
```

Play 1 ハルモニア / 恋ひ恋ふ縁: first-play mixer `busesStopped=0`, BGM routed, **VOX never in the play**. Overlay opened once (`1787146753` 13:39:39). Constructor still `[]`. Slider 100% is level, not the gate (§11).

Then they replay the same chart and leave. SongSelect resume:

```
InvalidOperationException: Cannot access Track without first calling LoadTrack
  at WorkingBeatmap.PrepareTrackForPreview
  at SongSelect.ensureTrackLooping
  at SongSelect.beginLooping
  at SongSelect.OnResuming
```

Play 1 resume does **not** crash (`song select bindable disabled=True` — same loaded gameplay instance). Play 2 attach is `disabled=False gameWide=disabled=True`: SongSelect.Beatmap.Value is a **different, unloaded** WorkingBeatmap. `beginLooping` runs **before** `ensureGlobalBeatmapValid`. That is why 切歌 red-screens / the next chart has no audio.

Separately, when VOX *is* in the play, leftover halt + `DestroyBuses` can leave `Bass.CurrentDevice` on the Uta mixer (`if (previous > 0)` skipped `Bass.DefaultDevice == -1`). Native VOX is a second osu TrackBass. After that leak it is created on the mixer device and is silent. Routed BGM still works. That is "BGM yes, 原唱 no" after 切歌.

**Facts:**

- `mods: []` + `enabled=False` + `slider=100%` is bug B's old signature. Overlay focus is still not `[VOX]`.
- Empty constructor mods after 切歌 is **not** an explicit VOX off. Treating `[]` as off dropped original vocals on every next chart.
- Native VOX + routed BGM are two graphs. Halt/DestroyBuses is legal for leftover BGM. It is **not** legal to then create native VOX on the leaked device.
- `ensureTrackLooping` on resume needs `TrackLoaded` on **SongSelect.Beatmap.Value**, not LastPlayed.

**Legal fix (this section):**

1. Persist original-vocals preference (`VOX` or the Play-original-vocals checkbox). `[]` after 切歌 keeps the last on-state. Only a live-mods falling edge, the checkbox, or remote VOX-off turns it off.
2. When BGM is already routed (latency / other device), route VOX on the same mixer. Do not keep a native TrackBass next to leftover halt.
3. Always restore `Bass.CurrentDevice`, including `-1`.
4. On the update-thread leave path, `LoadTrack` the instance SongSelect will `beginLooping`. Do **not** `Play()` it (§21). Do **not** LoadTrack every carousel hop (§17 / §19).

**Do not:** treat the ORIGINAL VOCALS slider as the gate (§11). **Do not:** stack another SelectedMods reflection hack without a log line `constructor=[] live=[] game=[VOX]` (§12). **Do not:** mixer drain variants (§1–§7). **Do not:** `changeBeatmap(LastPlayed)+Play()` (§21). **Do not:** Return the lease at LoadComplete (§23).

### 25. Do not verify leftover with the wrong screenshot / click host

osu already has the verification tools. Using COSMIC's screenshot of the other display, or xdotool into the auto-hidden toolbar / Edit, is not a new audio fact.

**Legal:**

- **F12** (`GlobalAction.TakeScreenshot`) writes `storage/screenshots/osu_*.jpg` of the osu framebuffer only.
- **TestScene / VisualTestRunner** (`osu.Game.Tests.OsuTestBrowser`, same as `osu.Game.Rulesets.Osu.Tests`). `dotnet run` the Uta test project. `TestSceneUtaOriginalVocals` is the empty-constructor-after-VOX contract.
- **Open a song the way osu tests do**, not via xdotool: `OsuGame.PresentBeatmap` after `BeatmapManager.Import` (`TestScenePresentBeatmap`, `TestSceneSongSelectNavigation`). Then `InputManager.Key(Enter)` to play. `TestSceneUtaPresentBeatmap` is that path with two real UTZ files. `--run-leftover` on VisualTestRunner.
- Live already-running osu: second `osu! /path/to.utz` is `ArchiveImportIPCChannel` (import). Present is the "Click to view" notification, not automatic. There is no IPC "play now".

**Do not:** `cosmic-screenshot` when Grok is on the other monitor. **Do not:** click ~65% width on the main menu (that is Edit). **Do not:** treat OS-level clicks as the way to open a chart — osu already has PresentBeatmap. **Do not:** treat a TestScene green as a live `mods: [VOX]` + `Uta vocals route ready` line — still read the runtime log for the real leftover graph.

### 26. VisualTestRunner `Default` mixer Parameter is not a load stall (`1787153108`)

`--run-leftover` log 15:25:26–15:26:47 looked like PlayerLoader hung for 80 seconds. The first real error was one line:

```
Uta microphone unavailable: Could not create output mixer for 'Default': Parameter
```

Then immediately:

```
entered SoloPlayer
allow UTZ player load 0/400
…
✔️ 400 repetitions   (15:26:47)
```

Facts:

- TestScene storage is empty. Mic/BGM output bind is `''`. BASS still lists a device **named** `Default`. `CreateMixerStream` on that index is `ILLPARAM`.
- The exception is caught in microphone `start()`. Player **did** load in ~1s. The 80s wait is `AddWaitStep(..., 400)` on a host that also spam-logs `Display at index (0) has no display modes` (~7 fps).
- After leave, `Completing PresentBeatmap … Snow Crystal` then `Game.Beatmap` still Asphodelos is a **carousel** check, not mixer leftover. osu's own test waits for `SoloSongSelect.CarouselItemsPresented`, not a 30-frame settle.

**Legal:** skip an *uninitialised* BASS name (including Default) when a real inited output exists; if MARANTZ is not `IsInitialized`, stay on osu's current device. Seed leftover TestScene with MARANTZ/AKG/−411/debug; wait on `player.IsLoaded` / `CarouselItemsPresented`. Create the Uta mixer like osu `BassAudioMixer` (44100, `MixerNonStop`). **Do not:** treat `WaitStep 400` as “UTZ is slow”. **Do not:** `Init` every ALSA node after the named device fails (`1787154543` HDMI/AKG/PipeWire Parameter spam). **Do not:** remap away from an already-inited `Default` to an unopened MARANTZ index — second-device Pulse Init is `Init`/`Parameter`. **Do not:** flood the log with per-try present-check lines.

### 27. `Update()`-after-leave `LoadTrack` is after `beginLooping` (`1787146753`)

`Player.Exit` is synchronous in the input handler:

```
PlayerLoader.OnExiting
ScreenStack.ScreenExited → UnbindAllBindablesSubTree (lease returned)
SongSelect.OnResuming → beginLooping → Track getter
```

`UtaGameplaySessionBridge.Update` only sees `!player.IsCurrentScreen()` on the **next** frame. `LoadTrack` there cannot prevent:

```
InvalidOperationException: Cannot access Track without first calling LoadTrack
  at SongSelect.beginLooping / OnResuming
```

Returning the lease from `UtaGlobalExtension.Dispose` is later still, and `LeasedBindable.Return` `UnbindAll`s. That is the TestScene case where `Completing PresentBeatmap Snow Crystal` never logs `Game-wide working beatmap updated`.

**Legal:** hook `ScreenStack.ScreenExited` once (after UnbindAll, before `OnResuming`), attach `songSelect.Beatmap`, destroy Uta playback, `LoadTrack` + `PrepareTrackForPreview` on that instance. **Do not:** Return the gameplay lease from Uta. **Do not:** treat `Update()`-when-Player-is-gone as OnResuming.

### 28. Fixed path and reproducible verification (`1787161065`, 2026-08-20)

The passing implementation differs from §§20–23 in three important ways:

1. `ScreenStack.ScreenExited` refreshes the **current** `SongSelect.Beatmap` after
   `PlayerLoader` has returned its lease and before `SongSelect.OnResuming` calls
   `beginLooping`. It stops the old TrackBass/MusicController and loads exactly
   that one resume instance. It never returns the lease from Uta.
2. A returned gameplay lease can leave `SongSelect.Beatmap` and Uta's cached
   game-wide copy detached. On a new song-select value, Uta mirrors the value,
   invokes `MusicController.changeBeatmap(newSelection)`, and starts that **new
   selection**. This is not the failed §21 leave path: it never calls
   `changeBeatmap(LastPlayed)+Play()`, and it never calls `LoadTrack()` on every
   private carousel `WorkingBeatmap`. `MusicController` owns the created track.
3. Routed VOX follows routed BGM, and every temporary `Bass.CurrentDevice`
   change is restored in `finally`, including `Bass.DefaultDevice == -1` and
   decoder/monitor creation failures.

The passing VisualTestRunner sequence used the real two UTZ files and osu's
`PresentBeatmap` path:

```text
Asphodelos, mods=[]
  Uta debug microphone: started ... name='AKG C44-USB Microphone: USB Audio'
  Uta halted leftover playback: streams=1 ... busesStopped=1

Snow Crystal, mods=[VOX]
  Uta mirrored and started song select preview: ... Snow Crystal
  Uta debug ruleset mods: [VOX] originalVocalsEnabled=True preferred=True
  Uta vocals route ready: ... routed=True native=False
  Uta halted leftover playback: streams=2 ... busesStopped=1

Return to Asphodelos preview
  Uta mirrored and started song select preview: Rena - Asphodelos
  selectedLoaded=True selectedRunning=True
  selectedTime=42275.9 musicRunning=True musicTime=42275.9
  play 1 track is stopped
  play 2 track is stopped
  ✅ TestSceneUtaPresentBeatmap completed
```

There were no `[error]`, `Cannot access Track without first calling LoadTrack`,
or other exceptions in `~/.local/share/osu-uta-visual-tests/logs/1787161065.runtime.log`.
The final 47-step version explicitly retained both gameplay `WorkingBeatmap`
instances and asserted each old `Track.IsRunning == false` after switching.
The normal suite also passed: **132/132**. The dedicated
`TestSceneUtaOriginalVocals` then completed all 23 steps in
`1787160990.runtime.log`: `[VOX]` set `preferred=True`, and the following
`mods: []` player logged `originalVocalsEnabled=True preferred=True`. Its only
error-level messages were the expected standalone-TestScene warning that
lazer's native volume overlay is absent; they are unrelated to audio playback.

#### NixOS / Wayland test-host notes

A plain `dotnet run` can fail with `SDL: No available video device` because the
NuGet SDL/BASS binaries `dlopen` libraries which are not on NixOS's default
loader path. The successful host included the Nix store `lib` paths for:
`alsa-lib`, `wayland`, `libxkbcommon`, `libdecor`, `libglvnd`, `libpulseaudio`,
and `libX11`. Missing `alsa-lib` produced the misleading assertion
`Bass did not provide any audio devices` even after SDL/Wayland was fixed.

The required physical test display is **DP-7 / EX-LDGC251UT, 1920×1080 at
239.888 Hz**, positioned to the right of HDMI-A-1. COSMIC's exact move-to-output
shortcut is **Super+Shift+Alt+Right** (not Super+Shift+Right). Sending the latter
left the host on logical 1920×1080 HDMI-A-1 (the 4K display at 200% scale), where
its low test-host frame rate caused false 10-second `AddUntilStep` timeouts.
Do not diagnose those timeouts as audio or UTZ load failures. After moving to
DP-7, the complete 47-step flow finished in about 24 seconds.

The TestScene must log the AKG start line before audio conclusions are accepted.
Its fallback output-device discovery may pick an HDMI BASS node when MARANTZ is
not exposed in fresh test storage; that is adequate for graph/lifecycle checks
but is not proof of the live speaker route. Live osu keeps the user's existing
MARANTZ output and AKG capture settings; do not overwrite them from the test.

### 29. Human rejection exposed two missing acceptance checks (`1787161203`)

The first automated acceptance was insufficient and the human correctly rejected
it. The live log proves that all three entered plays had:

```text
Uta debug ruleset mods: [] originalVocalsEnabled=False preferred=False
Uta vocals skipped: originalVocalsEnabled=False
Uta debug audio: ... vocals=n/a
```

`UserModSelectOverlay` appearing in the log did **not** mean VOX was selected.
This is the authoritative answer to “最后还是没有原唱”: no vocal decoder or
mixer failure occurred because the vocal graph was never requested.

The same log also exposed a device configuration error:

```text
mic='AKG C44-USB Microphone: USB Audio'
mic-output='MARANTZ M4U: USB Audio'
bgm-output='AKG C44-USB Microphone: USB Audio'
vocals-output='AKG C44-USB Microphone: USB Audio'
```

AKG is the required **capture** device, not the intended BGM/VOX output. The
runtime now repairs BGM/VOX outputs to the configured monitor output when both
were accidentally set to the capture device, and logs
`Uta repaired capture device used as playback output`.

The durable VOX fix now observes the current `SongSelect.Mods` after
`ScreenExited`, beyond the disposed first DrawableRuleset. A positive VOX edge
sets the process preference even if the next PlayerLoader constructor receives
`mods: []`. Required confirmation is:

```text
Uta game-wide selected mods changed: ... [VOX] ... vox=True preferred=True
Uta debug ruleset mods: ... originalVocalsEnabled=True preferred=True
Uta vocals route ready: ... routed=True native=False
Uta debug audio: ... vocals=<number>ms
```

Absence of the first line means the UI did not actually select VOX. Presence of
only a 100% slider line is still not acceptance.

The VisualTestRunner was strengthened: both plays must run past the initial gap
skip, play 2 must have a real vocals graph whose playback position advances,
and both old gameplay tracks must be stopped after switching. The final 51-step
acceptance passed **three consecutive times** on DP-7 with AKG capture:

- `1787161797.runtime.log`
- `1787161827.runtime.log`
- `1787161857.runtime.log`

Each contains `play 1 ran past gap skip`, `vox=True preferred=True`,
`play 2 vocals graph exists`, `play 2 ran past gap skip`, `play 2 vocals
advanced`, both old-track-stopped assertions, and
`✅ TestSceneUtaPresentBeatmap completed`, with no failed UntilStep or
`Cannot access Track` exception.

## Log checklist

- [ ] Play 1: no `previous=` line (LastPlayedBeatmap null).
- [ ] ESC: `busesStopped=1` then `ensurePlayingSelected` / which title in the next `Invalidating working beatmap cache`.
- [ ] Play 2: `previous='…' running=True|False` **and** `musicWasPlaying=True|False`.
- [ ] After Stop: `stillRunning=True|False` on session reset / leftover gameplay track stop.
- [ ] `mods: […]` — VOX present or not. Overlay focus is not enough.
- [ ] `vocals=n/a` vs `Uta vocals route ready`. After 切歌 with preference on: `originalVocalsEnabled=True` even if `mods: []`, and `Uta vocals route ready … routed=True` when BGM is routed.
- [ ] `Cannot access Track without first calling LoadTrack` — regression.
- [ ] `Uta original vocals on … preferred=True` after a 切歌 that kept VOX without re-selecting it.

## Code touched

- `UtaAudioRouter`: halt / `ChannelStop` / `DestroyBuses`. Mixer path is done; do not add drain variants.
- `UtaAudioController.beginFreshSession`: DestroyBuses, Stop previous loaded Track if different id, MusicController.Stop(false).
- `UtaRulesetRuntime`: §23 — do not watch a dead SongSelect; do not Return the lease at LoadComplete; UserPauseRequested must be the backing field or MainMenu `EnsurePlayingSomething` restarts leftover. §24 — persist original-vocals preference; `LoadTrack` SongSelect.Beatmap.Value on leave.
- `UtaAudioController` / `UtaAudioMath`: §24 — route VOX with routed BGM; never leave native VOX on a leaked CurrentDevice.
- `UtaMicrophoneHandler`: keep non-throwing device info.
- `LeaveForQueuedChart`: do not go back to `Player.Restart`.
