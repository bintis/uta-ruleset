# Gameplay clock architecture

`GameplayClockContainer` is uta!'s single authoritative song-time source. No
ruleset-owned audio stream, pitch callback or UI timer is allowed to advance its
own song clock.

## Ownership

| Concern | Owner | Rule |
|---|---|---|
| Play/pause, seek, loop/retry epoch and MOD rate | lazer `GameplayClockContainer` | Only the gameplay clock changes canonical song position. |
| Main BGM | lazer `WorkingBeatmap.Track` | This is the master track and gameplay clock source. |
| Routed BGM and vocals | `UtaAudioController` | They are slaves: each update and seek follows the master clock, including pause and rate. |
| Transpose | `UtaAudioController` adjustment bindables | Frequency and inverse tempo are applied as a pair; neither changes song time. |
| Accompaniment latency | `UtaAudioController.sourceTime()` | It offsets routed accompaniment only. It never changes the master clock or score time. |
| Lyrics latency | `UtaLyricsTimeline` | Presentation-only offset; it never changes audio, pitch or scoring. |
| Microphone latency and scoring time | `UtaGameplayTimelineMapper` | Capture timestamps are anchored to the gameplay clock and rate. Backward seek/loop/retry starts a new epoch. |
| Live pitch display | `UtaInputManager` | It derives a display position from the current gameplay time and calibrated capture age; it owns no playback state. |

## Seek, pause and rate invariants

1. A seek invokes `UtaAudioController.synchroniseAfterSeek()` and creates a new
   scoring timeline epoch when the seek moves backwards. No old microphone frame
   can be scored in the new epoch.
2. A pause stops all slave streams. Resume reconstructs any routed stream and
   resynchronises it before playback starts.
3. Nightcore/Daycore and Transpose adjust playback factors only. Routed BGM,
   native/routed vocals, lyrics, pitch guide and scoring all continue to read
   the same gameplay time.
4. Route recovery may move a stream to lazer's default output, but it preserves
   its position/rate/volume and never replaces the gameplay clock.

The deterministic runtime-hardening suite exercises Transpose, OCT, 0.75×/1×/
1.5× rates, microphone latency, seeks, loops and retry epochs. Real-game checks
are listed in [`TESTING.md`](TESTING.md).
