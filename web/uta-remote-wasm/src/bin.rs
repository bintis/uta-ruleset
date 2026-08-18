use crate::proto::{LibrarySong, LoopState, Mixer, QueueEntry, Snapshot};

pub const MAGIC: u8 = b'U';
pub const VERSION: u8 = 1;

pub const KIND_COMMAND: u8 = 1;
pub const KIND_ACK: u8 = 2;
pub const KIND_ERROR: u8 = 3;
pub const KIND_WELCOME: u8 = 4;
pub const KIND_RESUMED: u8 = 5;
pub const KIND_STATE: u8 = 6;
pub const KIND_QUEUE: u8 = 7;
pub const KIND_RESULT: u8 = 8;
pub const KIND_TRACE: u8 = 9;

pub struct Incoming {
    pub kind: u8,
    pub session_id: Option<String>,
    pub session_secret: Option<String>,
    pub role: Option<String>,
    pub snapshot: Option<Snapshot>,
    pub entries: Option<Vec<QueueEntry>>,
    pub auto_advance: Option<bool>,
    pub request_id: Option<String>,
    pub accepted: Option<bool>,
    pub error: Option<String>,
    pub library: Option<Vec<LibrarySong>>,
}

pub fn command_id(name: &str) -> u8 {
    match name {
        "ping" => 1,
        "play" => 2,
        "pause" => 3,
        "togglePlayback" => 4,
        "seek" => 5,
        "seekRelative" => 6,
        "speed" => 7,
        "setLoopA" => 8,
        "setLoopB" => 9,
        "clearLoop" => 10,
        "previousPhrase" => 11,
        "nextPhrase" => 12,
        "retryPhrase" => 13,
        "loopPhrase" => 14,
        "bgmVolume" => 15,
        "vocalsVolume" => 16,
        "monitorVolume" => 17,
        "transpose" => 18,
        "octaveFold" => 19,
        "originalVocals" => 20,
        "microphoneLatency" => 21,
        "accompanimentLatency" => 22,
        "lyricsLatency" => 23,
        "disconnect" => 24,
        "librarySearch" => 25,
        "queueAdd" => 26,
        "queueRemove" => 27,
        "queueClear" => 28,
        "queuePlayNow" => 29,
        "skipCurrent" => 30,
        "skipToNext" => 31,
        "queueAddNext" => 32,
        "queueMove" => 33,
        "queueMoveToTop" => 34,
        "queueMoveToBottom" => 35,
        "autoAdvance" => 36,
        "setMod" => 37,
        "queueConfigure" => 38,
        _ => 0,
    }
}

pub fn encode_command(
    sequence: i32,
    name: &str,
    value: Option<f64>,
    enabled: Option<bool>,
    text: Option<&str>,
    request_id: Option<&str>,
    options: Option<(f64, i32, &[String])>,
) -> Vec<u8> {
    let mut body = Vec::new();
    write_i64(&mut body, sequence as i64);
    body.push(command_id(name));
    let mut flags = 0u8;
    if value.is_some() {
        flags |= 1;
    }
    if enabled.is_some() {
        flags |= 2;
    }
    if text.is_some() {
        flags |= 4;
    }
    if request_id.is_some() {
        flags |= 8;
    }
    if options.is_some() {
        flags |= 16;
    }
    body.push(flags);
    if let Some(value) = value {
        write_f64(&mut body, value);
    }
    if let Some(enabled) = enabled {
        body.push(u8::from(enabled));
    }
    if let Some(text) = text {
        write_str(&mut body, text);
    }
    if let Some(request_id) = request_id {
        write_str(&mut body, request_id);
    }
    if let Some((speed, transpose, mods)) = options {
        write_f64(&mut body, speed);
        body.push(transpose as u8);
        body.push(mods.len().min(32) as u8);
        for acronym in mods.iter().take(32) {
            write_str(&mut body, acronym);
        }
    }
    frame(KIND_COMMAND, &body)
}

pub fn encode_trace(event: &str, detail: &str) -> Vec<u8> {
    let mut body = Vec::new();
    write_str(&mut body, event);
    write_str(&mut body, detail);
    frame(KIND_TRACE, &body)
}

pub fn parse(bytes: &[u8]) -> Option<Incoming> {
    if bytes.len() < 7 || bytes[0] != MAGIC || bytes[1] != VERSION {
        return None;
    }
    let length = read_i32_at(bytes, 3)? as usize;
    if 7 + length > bytes.len() {
        return None;
    }
    let kind = bytes[2];
    let mut r = Reader {
        data: &bytes[7..7 + length],
    };
    let mut msg = Incoming {
        kind,
        session_id: None,
        session_secret: None,
        role: None,
        snapshot: None,
        entries: None,
        auto_advance: None,
        request_id: None,
        accepted: None,
        error: None,
        library: None,
    };
    match kind {
        KIND_WELCOME => {
            msg.session_id = Some(r.str()?);
            msg.session_secret = Some(r.str()?);
            msg.role = Some(if r.u8()? == 1 { "spectator".into() } else { "controller".into() });
            let _version = r.i32()?;
            msg.snapshot = Some(read_snapshot(&mut r)?);
        }
        KIND_RESUMED => {
            msg.role = Some(if r.u8()? == 1 { "spectator".into() } else { "controller".into() });
            msg.snapshot = Some(read_snapshot(&mut r)?);
        }
        KIND_STATE => msg.snapshot = Some(read_snapshot(&mut r)?),
        KIND_QUEUE => {
            let _rev = r.i64()?;
            msg.auto_advance = Some(r.bool()?);
            msg.entries = Some(read_queue(&mut r)?);
        }
        KIND_RESULT => {
            msg.request_id = Some(r.str()?);
            msg.accepted = Some(r.bool()?);
            msg.error = Some(r.str()?);
            msg.library = Some(read_library(&mut r)?);
        }
        KIND_ERROR => {
            let _seq = r.i64()?;
            msg.error = Some(r.str()?);
        }
        KIND_ACK => {}
        _ => return None,
    }
    Some(msg)
}

fn read_snapshot(r: &mut Reader<'_>) -> Option<Snapshot> {
    let _revision = r.i64()?;
    let song_time = r.f64()?;
    let song_length = r.f64()?;
    let paused = r.bool()?;
    let speed = r.f64()?;
    let phrase_index = r.i32()?;
    let phrase_count = r.i32()?;
    let score = r.f64()?;
    let pitch_similarity = r.f64()?;
    let voice_active = r.bool()?;
    let detected = r.f64()?;
    let loop_a = r.f64()?;
    let loop_b = r.f64()?;
    let loop_phrase = r.bool()?;
    let mixer = Mixer {
        background_music: r.f64()?,
        original_vocals: r.f64()?,
        microphone_monitor: r.f64()?,
        transpose: r.i32()?,
        octave_fold: r.bool()?,
        original_vocals_enabled: r.bool()?,
        microphone_latency: r.f64()?,
        accompaniment_latency: r.f64()?,
        lyrics_latency: r.f64()?,
    };
    let auto_advance_enabled = r.bool()?;
    let _queue_revision = r.i64()?;
    let notice = empty_to_none(r.str()?);
    let current_lyrics = r.str()?;
    let next_lyrics = empty_to_none(r.str()?);
    let song_title = empty_to_none(r.str()?);
    let song_artist = empty_to_none(r.str()?);
    let song_difficulty = empty_to_none(r.str()?);
    let song_creator = empty_to_none(r.str()?);
    let queue = read_queue(r)?;
    let mod_count = r.u16()? as usize;
    let mut active_mods = Vec::new();
    for _ in 0..mod_count {
        let acronym = r.str()?;
        let _name = r.str()?;
        let on = r.bool()?;
        if on {
            active_mods.push(acronym);
        }
    }
    Some(Snapshot {
        song_time,
        song_length,
        paused,
        speed,
        phrase_index,
        phrase_count,
        current_lyrics,
        next_lyrics,
        detected_pitch_midi: if detected.is_finite() { Some(detected) } else { None },
        pitch_similarity,
        voice_active,
        score,
        loop_field: LoopState {
            a: if loop_a.is_finite() { Some(loop_a) } else { None },
            b: if loop_b.is_finite() { Some(loop_b) } else { None },
            current_phrase: loop_phrase,
        },
        mixer,
        queue,
        auto_advance_enabled,
        notice,
        song_title,
        song_artist,
        song_difficulty,
        song_creator,
        active_mods,
    })
}

fn read_queue(r: &mut Reader<'_>) -> Option<Vec<QueueEntry>> {
    let count = r.u16()? as usize;
    let mut entries = Vec::with_capacity(count);
    for _ in 0..count {
        let id = r.str()?;
        let title = r.str()?;
        let artist = r.str()?;
        let difficulty_name = empty_to_none(r.str()?);
        let length_ms = r.f64()?;
        let speed = r.f64()?;
        let transpose = r.i32()?;
        let mod_count = r.u8()? as usize;
        let mut mods = Vec::with_capacity(mod_count);
        for _ in 0..mod_count {
            mods.push(r.str()?);
        }
        if !id.is_empty() {
            entries.push(QueueEntry {
                id,
                title,
                artist,
                difficulty_name,
                length_ms,
                speed,
                transpose,
                mods,
            });
        }
    }
    Some(entries)
}

fn read_library(r: &mut Reader<'_>) -> Option<Vec<LibrarySong>> {
    let count = r.u16()? as usize;
    let mut songs = Vec::with_capacity(count);
    for _ in 0..count {
        let beatmap_id = r.str()?;
        let title = r.str()?;
        let artist = r.str()?;
        let difficulty_name = empty_to_none(r.str()?);
        let creator = empty_to_none(r.str()?);
        let length_ms = r.f64()?;
        if !beatmap_id.is_empty() {
            songs.push(LibrarySong {
                beatmap_id,
                title,
                artist,
                difficulty_name,
                creator,
                length_ms,
            });
        }
    }
    Some(songs)
}

fn empty_to_none(value: String) -> Option<String> {
    if value.is_empty() {
        None
    } else {
        Some(value)
    }
}

fn frame(kind: u8, payload: &[u8]) -> Vec<u8> {
    let mut out = Vec::with_capacity(7 + payload.len());
    out.push(MAGIC);
    out.push(VERSION);
    out.push(kind);
    write_i32(&mut out, payload.len() as i32);
    out.extend_from_slice(payload);
    out
}

fn write_i32(out: &mut Vec<u8>, value: i32) {
    out.extend_from_slice(&value.to_le_bytes());
}

fn write_i64(out: &mut Vec<u8>, value: i64) {
    out.extend_from_slice(&value.to_le_bytes());
}

fn write_f64(out: &mut Vec<u8>, value: f64) {
    out.extend_from_slice(&value.to_le_bytes());
}

fn write_str(out: &mut Vec<u8>, value: &str) {
    let bytes = value.as_bytes();
    let len = bytes.len().min(u16::MAX as usize);
    out.extend_from_slice(&(len as u16).to_le_bytes());
    out.extend_from_slice(&bytes[..len]);
}

fn read_i32_at(bytes: &[u8], offset: usize) -> Option<i32> {
    let slice = bytes.get(offset..offset + 4)?;
    Some(i32::from_le_bytes(slice.try_into().ok()?))
}

struct Reader<'a> {
    data: &'a [u8],
}

impl<'a> Reader<'a> {
    fn u8(&mut self) -> Option<u8> {
        let value = *self.data.first()?;
        self.data = &self.data[1..];
        Some(value)
    }

    fn u16(&mut self) -> Option<u16> {
        let slice = self.data.get(..2)?;
        self.data = &self.data[2..];
        Some(u16::from_le_bytes(slice.try_into().ok()?))
    }

    fn i32(&mut self) -> Option<i32> {
        let slice = self.data.get(..4)?;
        self.data = &self.data[4..];
        Some(i32::from_le_bytes(slice.try_into().ok()?))
    }

    fn i64(&mut self) -> Option<i64> {
        let slice = self.data.get(..8)?;
        self.data = &self.data[8..];
        Some(i64::from_le_bytes(slice.try_into().ok()?))
    }

    fn f64(&mut self) -> Option<f64> {
        let slice = self.data.get(..8)?;
        self.data = &self.data[8..];
        Some(f64::from_le_bytes(slice.try_into().ok()?))
    }

    fn bool(&mut self) -> Option<bool> {
        Some(self.u8()? != 0)
    }

    fn str(&mut self) -> Option<String> {
        let len = self.u16()? as usize;
        let slice = self.data.get(..len)?;
        self.data = &self.data[len..];
        Some(String::from_utf8_lossy(slice).into_owned())
    }
}
