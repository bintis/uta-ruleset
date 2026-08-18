#[derive(Clone, Debug, Default)]
pub struct Mixer {
    pub background_music: f64,
    pub original_vocals: f64,
    pub microphone_monitor: f64,
    pub transpose: i32,
    pub octave_fold: bool,
    pub original_vocals_enabled: bool,
    pub microphone_latency: f64,
    pub accompaniment_latency: f64,
    pub lyrics_latency: f64,
}

#[derive(Clone, Debug, Default)]
pub struct LoopState {
    pub a: Option<f64>,
    pub b: Option<f64>,
    pub current_phrase: bool,
}

#[derive(Clone, Debug)]
pub struct QueueEntry {
    pub id: String,
    pub title: String,
    pub artist: String,
    pub difficulty_name: Option<String>,
    pub length_ms: f64,
    pub speed: f64,
    pub transpose: i32,
    pub mods: Vec<String>,
}

#[derive(Clone, Debug)]
pub struct LibrarySong {
    pub beatmap_id: String,
    pub title: String,
    pub artist: String,
    pub difficulty_name: Option<String>,
    pub creator: Option<String>,
    pub length_ms: f64,
}

#[derive(Clone, Debug, Default)]
pub struct Snapshot {
    pub song_time: f64,
    pub song_length: f64,
    pub paused: bool,
    pub speed: f64,
    pub phrase_index: i32,
    pub phrase_count: i32,
    pub current_lyrics: String,
    pub next_lyrics: Option<String>,
    pub detected_pitch_midi: Option<f64>,
    pub pitch_similarity: f64,
    pub voice_active: bool,
    pub score: f64,
    pub loop_field: LoopState,
    pub mixer: Mixer,
    pub queue: Vec<QueueEntry>,
    pub auto_advance_enabled: bool,
    pub notice: Option<String>,
    pub song_title: Option<String>,
    pub song_artist: Option<String>,
    pub song_difficulty: Option<String>,
    pub song_creator: Option<String>,
    pub active_mods: Vec<String>,
}

impl Snapshot {
    pub fn r#loop(&self) -> LoopState {
        self.loop_field.clone()
    }

    pub fn has_gameplay(&self) -> bool {
        self.notice.as_deref().unwrap_or("").is_empty()
    }
}

#[derive(Debug, Default)]
pub struct Envelope {
    pub kind: String,
    pub role: Option<String>,
    pub snapshot: Option<Snapshot>,
    pub entries: Option<Vec<QueueEntry>>,
    pub auto_advance_enabled: Option<bool>,
    pub request_id: Option<String>,
    pub accepted: Option<bool>,
    pub error: Option<String>,
    pub library: Option<Vec<LibrarySong>>,
}

pub fn parse_envelope(text: &str) -> Option<Envelope> {
    let mut msg = Envelope {
        kind: string_field(text, "type")?,
        ..Envelope::default()
    };
    msg.role = string_field(text, "role");
    msg.request_id = string_field(text, "requestId");
    msg.accepted = bool_field(text, "accepted");
    msg.error = string_field(text, "error");
    msg.auto_advance_enabled = bool_field(text, "autoAdvanceEnabled");
    if let Some(raw) = object_field(text, "snapshot") {
        msg.snapshot = Some(parse_snapshot(raw));
    }
    if let Some(raw) = array_field(text, "entries") {
        msg.entries = Some(parse_queue_array(raw));
    }
    if let Some(raw) = array_field(text, "library") {
        msg.library = Some(parse_library_array(raw));
    }
    Some(msg)
}

fn parse_snapshot(raw: &str) -> Snapshot {
    let loop_raw = object_field(raw, "loop").unwrap_or("");
    let mixer_raw = object_field(raw, "mixer").unwrap_or("");
    Snapshot {
        song_time: number_field(raw, "songTime").unwrap_or(0.0),
        song_length: number_field(raw, "songLength").unwrap_or(0.0),
        paused: bool_field(raw, "paused").unwrap_or(true),
        speed: number_field(raw, "speed").unwrap_or(1.0),
        phrase_index: number_field(raw, "phraseIndex").unwrap_or(-1.0) as i32,
        phrase_count: number_field(raw, "phraseCount").unwrap_or(0.0) as i32,
        current_lyrics: string_field(raw, "currentLyrics").unwrap_or_default(),
        next_lyrics: string_field(raw, "nextLyrics"),
        detected_pitch_midi: number_field(raw, "detectedPitchMidi"),
        pitch_similarity: number_field(raw, "pitchSimilarity").unwrap_or(0.0),
        voice_active: bool_field(raw, "voiceActive").unwrap_or(false),
        score: number_field(raw, "score").unwrap_or(0.0),
        loop_field: LoopState {
            a: number_field(loop_raw, "a"),
            b: number_field(loop_raw, "b"),
            current_phrase: bool_field(loop_raw, "currentPhrase").unwrap_or(false),
        },
        mixer: Mixer {
            background_music: number_field(mixer_raw, "backgroundMusic").unwrap_or(1.0),
            original_vocals: number_field(mixer_raw, "originalVocals").unwrap_or(0.55),
            microphone_monitor: number_field(mixer_raw, "microphoneMonitor").unwrap_or(0.35),
            transpose: number_field(mixer_raw, "transpose").unwrap_or(0.0) as i32,
            octave_fold: bool_field(mixer_raw, "octaveFold").unwrap_or(false),
            original_vocals_enabled: bool_field(mixer_raw, "originalVocalsEnabled").unwrap_or(false),
            microphone_latency: number_field(mixer_raw, "microphoneLatency").unwrap_or(0.0),
            accompaniment_latency: number_field(mixer_raw, "accompanimentLatency").unwrap_or(0.0),
            lyrics_latency: number_field(mixer_raw, "lyricsLatency").unwrap_or(0.0),
        },
        queue: array_field(raw, "queue").map(parse_queue_array).unwrap_or_default(),
        auto_advance_enabled: bool_field(raw, "autoAdvanceEnabled").unwrap_or(false),
        notice: string_field(raw, "notice"),
        song_title: string_field(raw, "songTitle"),
        song_artist: string_field(raw, "songArtist"),
        song_difficulty: string_field(raw, "songDifficulty"),
        song_creator: string_field(raw, "songCreator"),
        active_mods: Vec::new(),
    }
}

fn parse_queue_array(raw: &str) -> Vec<QueueEntry> {
    objects(raw)
        .into_iter()
        .map(|item| QueueEntry {
            id: string_field(item, "id").unwrap_or_default(),
            title: string_field(item, "title").unwrap_or_default(),
            artist: string_field(item, "artist").unwrap_or_default(),
            difficulty_name: string_field(item, "difficultyName"),
            length_ms: number_field(item, "lengthMs").unwrap_or(0.0),
            speed: number_field(item, "speed").unwrap_or(1.0),
            transpose: number_field(item, "transpose").unwrap_or(0.0) as i32,
            mods: array_field(item, "mods").map(string_array).unwrap_or_default(),
        })
        .filter(|entry| !entry.id.is_empty())
        .collect()
}

fn parse_library_array(raw: &str) -> Vec<LibrarySong> {
    objects(raw)
        .into_iter()
        .map(|item| LibrarySong {
            beatmap_id: string_field(item, "beatmapId").unwrap_or_default(),
            title: string_field(item, "title").unwrap_or_default(),
            artist: string_field(item, "artist").unwrap_or_default(),
            difficulty_name: string_field(item, "difficultyName"),
            creator: string_field(item, "creator"),
            length_ms: number_field(item, "lengthMs").unwrap_or(0.0),
        })
        .filter(|song| !song.beatmap_id.is_empty())
        .collect()
}

pub fn command(
    sequence: i32,
    name: &str,
    value: Option<f64>,
    enabled: Option<bool>,
    text: Option<&str>,
    request_id: Option<&str>,
    options: Option<&str>,
) -> String {
    let mut body = format!(
        "{{\"type\":\"command\",\"sequence\":{sequence},\"command\":\"{}\"",
        escape(name)
    );
    if let Some(value) = value {
        body.push_str(&format!(",\"value\":{value}"));
    }
    if let Some(enabled) = enabled {
        body.push_str(&format!(",\"enabled\":{}", if enabled { "true" } else { "false" }));
    }
    if let Some(text) = text {
        body.push_str(&format!(",\"text\":\"{}\"", escape(text)));
    }
    if let Some(request_id) = request_id {
        body.push_str(&format!(",\"requestId\":\"{}\"", escape(request_id)));
    }
    if let Some(options) = options {
        body.push_str(",\"options\":");
        body.push_str(options);
    }
    body.push('}');
    body
}

pub fn options_json(speed: f64, transpose: i32, mods: &[String]) -> String {
    let joined = mods
        .iter()
        .map(|mod_name| format!("\"{}\"", escape(mod_name)))
        .collect::<Vec<_>>()
        .join(",");
    format!("{{\"speed\":{speed},\"transpose\":{transpose},\"mods\":[{joined}]}}")
}

fn string_field(raw: &str, key: &str) -> Option<String> {
    let rest = after_key(raw, key)?;
    parse_string(rest)
}

fn number_field(raw: &str, key: &str) -> Option<f64> {
    let rest = after_key(raw, key)?;
    parse_number(rest)
}

fn bool_field(raw: &str, key: &str) -> Option<bool> {
    let rest = after_key(raw, key)?.trim_start();
    if rest.starts_with("true") {
        Some(true)
    } else if rest.starts_with("false") {
        Some(false)
    } else {
        None
    }
}

fn object_field<'a>(raw: &'a str, key: &str) -> Option<&'a str> {
    let rest = after_key(raw, key)?.trim_start();
    extract_balanced(rest, '{', '}')
}

fn array_field<'a>(raw: &'a str, key: &str) -> Option<&'a str> {
    let rest = after_key(raw, key)?.trim_start();
    extract_balanced(rest, '[', ']')
}

fn after_key<'a>(raw: &'a str, key: &str) -> Option<&'a str> {
    let needle = format!("\"{key}\"");
    let mut from = 0;
    while let Some(rel) = raw[from..].find(&needle) {
        let at = from + rel + needle.len();
        let rest = raw[at..].trim_start();
        if let Some(stripped) = rest.strip_prefix(':') {
            return Some(stripped);
        }
        from = at;
    }
    None
}

fn parse_string(raw: &str) -> Option<String> {
    let rest = raw.trim_start();
    if rest.starts_with("null") {
        return None;
    }
    let bytes = rest.as_bytes();
    if bytes.first().copied() != Some(b'"') {
        return None;
    }
    let mut out = String::new();
    let mut i = 1;
    while i < bytes.len() {
        match bytes[i] {
            b'"' => return Some(out),
            b'\\' if i + 1 < bytes.len() => {
                i += 1;
                match bytes[i] {
                    b'"' => out.push('"'),
                    b'\\' => out.push('\\'),
                    b'/' => out.push('/'),
                    b'n' => out.push('\n'),
                    b'r' => out.push('\r'),
                    b't' => out.push('\t'),
                    b'u' if i + 4 < bytes.len() => {
                        let hex = rest.get(i + 1..i + 5)?;
                        if let Ok(code) = u32::from_str_radix(hex, 16) {
                            if let Some(ch) = char::from_u32(code) {
                                out.push(ch);
                            }
                        }
                        i += 4;
                    }
                    other => out.push(other as char),
                }
            }
            c => out.push(c as char),
        }
        i += 1;
    }
    None
}

fn parse_number(raw: &str) -> Option<f64> {
    let rest = raw.trim_start();
    if rest.starts_with("null") {
        return None;
    }
    let end = rest
        .find(|c: char| !(c.is_ascii_digit() || matches!(c, '-' | '+' | '.' | 'e' | 'E')))
        .unwrap_or(rest.len());
    rest[..end].parse().ok()
}

fn extract_balanced(raw: &str, open: char, close: char) -> Option<&str> {
    let rest = raw.trim_start();
    if !rest.starts_with(open) {
        return None;
    }
    let mut depth = 0;
    let mut in_string = false;
    let mut escape = false;
    for (index, ch) in rest.char_indices() {
        if in_string {
            if escape {
                escape = false;
            } else if ch == '\\' {
                escape = true;
            } else if ch == '"' {
                in_string = false;
            }
            continue;
        }
        match ch {
            '"' => in_string = true,
            c if c == open => depth += 1,
            c if c == close => {
                depth -= 1;
                if depth == 0 {
                    return Some(&rest[..=index]);
                }
            }
            _ => {}
        }
    }
    None
}

fn objects(raw: &str) -> Vec<&str> {
    let inner = raw.trim().trim_start_matches('[').trim_end_matches(']');
    let mut items = Vec::new();
    let mut from = 0;
    while from < inner.len() {
        let slice = inner[from..].trim_start();
        let skipped = inner.len() - from - slice.len();
        if let Some(object) = extract_balanced(slice, '{', '}') {
            items.push(object);
            from += skipped + object.len();
        } else {
            break;
        }
        if let Some(rel) = inner[from..].find(',') {
            from += rel + 1;
        }
    }
    items
}

fn string_array(raw: &str) -> Vec<String> {
    let inner = raw.trim().trim_start_matches('[').trim_end_matches(']');
    let mut items = Vec::new();
    let mut rest = inner;
    while let Some(value) = parse_string(rest) {
        items.push(value);
        if let Some(after) = rest.find(',') {
            rest = &rest[after + 1..];
        } else {
            break;
        }
    }
    items
}

fn escape(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    for ch in value.chars() {
        match ch {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            c => out.push(c),
        }
    }
    out
}
