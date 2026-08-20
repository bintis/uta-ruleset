use crate::bin::{
    encode_command, encode_trace, parse as parse_bin, KIND_ERROR, KIND_QUEUE, KIND_RESULT,
    KIND_RESUMED, KIND_STATE, KIND_WELCOME,
};
use crate::i18n::{t, Lang};
use crate::proto::{parse_envelope, LibrarySong, QueueEntry, Snapshot};
use crate::{
    clip, fill_rect, fill_text, log, measure, persist_theme, search_rect, send, set_remembered,
    store_seq, store_session, stroke_rect, unclip,
};

const BG: u32 = 0xFF16161C;
const HEADER: u32 = 0xF216161C;
const PANEL: u32 = 0xFF2A2433;
const PANEL2: u32 = 0xFF33303C;
const ROW: u32 = 0xFF221E28;
const LINE: u32 = 0xFF4A4456;
const TEXT: u32 = 0xFFFFFFFF;
const MUTED: u32 = 0xFFB0A8B8;
const PINK: u32 = 0xFFE846A0;
const BLUE: u32 = 0xFF66CCFF;
const GOLD: u32 = 0xFFFFCC22;
const DANGER: u32 = 0xFFFF4B67;
const OK: u32 = 0xFFB3FF66;
const PURPLE: u32 = 0xFF8866EE;
const DIM: u32 = 0xCC0A0A0E;
const TAB_BG: u32 = 0xF21A1A22;

const PAGE_LIST: i32 = 0;
const PAGE_CONTROL: i32 = 1;
const PAGE_QUEUE: i32 = 2;
const PAGE_INFO: i32 = 3;
const PAGE_COUNT: i32 = 4;
const HEADER_H: f32 = 36.0;
const TAB_H: f32 = 0.0;
const QUEUE_ROW: f32 = 86.0;
const SONG_ROW: f32 = 70.0;

#[derive(Clone, Debug)]
enum Action {
    PlayPause,
    Restart,
    Skip,
    NextSong,
    PrevPhrase,
    Retry,
    NextPhrase,
    KeyDelta(i32),
    KeyReset,
    SpeedDelta(f64),
    SpeedReset,
    SeekTo(f64),
    OpenSong(String),
    CloseSheet,
    QueueAdd,
    QueueAddNext,
    PlayNow,
    ToggleMod(String),
    SheetKey(i32),
    SheetSpeed(f64),
    QueuePlay(String),
    QueueRemove(String),
    QueueGrab(usize),
    QueueClear,
    QueueEdit(usize),
    AutoAdvance,
    Mixer(&'static str, f64),
    Latency(&'static str, f64),
    LoopA,
    LoopB,
    ClearLoop,
    LoopPhrase,
    Octave,
    Vocals,
    Language,
    Disconnect,
    ConfirmYes,
    ConfirmNo,
    Page(i32),
    ToggleSearch,
    RememberDevice,
    Back,
    QueueMenu,
    InfoAudio,
    InfoAppearance,
    InfoBack,
    Theme(bool),
}

#[derive(Clone, Copy)]
enum Confirm {
    Skip,
    Clear,
}

#[derive(Clone, Copy)]
enum SheetKind {
    Add,
    Edit,
}

#[derive(Clone, Copy, PartialEq, Eq)]
enum InfoView {
    Overview,
    Audio,
    Appearance,
}

struct Sheet {
    kind: SheetKind,
    id: String,
    title: String,
    artist: String,
    speed: f64,
    transpose: i32,
    mods: Vec<(String, String, bool)>,
}

struct Hit {
    x: f32,
    y: f32,
    w: f32,
    h: f32,
    action: Action,
}

struct Toast {
    text: String,
    error: bool,
    until: f64,
}

#[derive(Clone, Copy)]
enum Axis {
    None,
    Horiz,
    Vert,
}

#[derive(Clone, Copy)]
enum DragKind {
    None,
    Pager,
    Scroll,
    Seek,
    Slider,
    Reorder,
}

pub struct App {
    width: f32,
    height: f32,
    inset_t: f32,
    inset_b: f32,
    reduced: bool,
    lang: Lang,
    page: f32,
    target: i32,
    connected: bool,
    role: String,
    snapshot: Snapshot,
    library: Vec<LibrarySong>,
    library_offset: usize,
    library_loading: bool,
    library_has_more: bool,
    queue: Vec<QueueEntry>,
    search: String,
    sequence: i32,
    request: u32,
    pending: Option<(String, &'static str)>,
    play_after_add: bool,
    add_next: bool,
    sheet: Option<Sheet>,
    confirm: Option<Confirm>,
    toast: Option<Toast>,
    hits: Vec<Hit>,
    scroll: [f32; 4],
    scroll_vel: [f32; 4],
    scroll_max: [f32; 4],
    vel_samples: Vec<(f64, f32)>,
    last_frame: f64,
    pointer_down: bool,
    px: f32,
    py: f32,
    sx: f32,
    sy: f32,
    start_page: f32,
    start_scroll: f32,
    axis: Axis,
    drag: DragKind,
    press_action: Option<Action>,
    last_seek_sent: f64,
    last_nav: f64,
    now: f64,
    search_box: (f32, f32, f32, f32),
    reorder_from: Option<usize>,
    reorder_origin_y: f32,
    reorder_finger_y: f32,
    reorder_list_y: f32,
    search_open: bool,
    remember: bool,
    queue_menu: bool,
    info_view: InfoView,
    light_theme: bool,
    skip_hold_started: Option<f64>,
}

impl App {
    pub fn new(lang: Lang, reduced: bool, sequence: i32, remember: bool) -> Self {
        Self {
            width: 390.0,
            height: 844.0,
            inset_t: 0.0,
            inset_b: 0.0,
            reduced,
            lang,
            page: PAGE_CONTROL as f32,
            target: PAGE_CONTROL,
            connected: false,
            role: "controller".into(),
            snapshot: Snapshot::default(),
            library: Vec::new(),
            library_offset: 0,
            library_loading: false,
            library_has_more: false,
            queue: Vec::new(),
            search: String::new(),
            sequence,
            request: 1,
            pending: None,
            play_after_add: false,
            add_next: false,
            sheet: None,
            confirm: None,
            toast: None,
            hits: Vec::new(),
            scroll: [0.0; 4],
            scroll_vel: [0.0; 4],
            scroll_max: [0.0; 4],
            vel_samples: Vec::new(),
            last_frame: 0.0,
            pointer_down: false,
            px: 0.0,
            py: 0.0,
            sx: 0.0,
            sy: 0.0,
            start_page: PAGE_CONTROL as f32,
            start_scroll: 0.0,
            axis: Axis::None,
            drag: DragKind::None,
            press_action: None,
            last_seek_sent: 0.0,
            last_nav: 0.0,
            now: 0.0,
            search_box: (0.0, 0.0, 0.0, 0.0),
            reorder_from: None,
            reorder_origin_y: 0.0,
            reorder_finger_y: 0.0,
            reorder_list_y: 0.0,
            search_open: false,
            remember,
            queue_menu: false,
            info_view: InfoView::Overview,
            light_theme: false,
            skip_hold_started: None,
        }
    }

    pub fn resize(&mut self, w: f32, h: f32, inset_t: f32, inset_b: f32) {
        self.width = w.max(240.0);
        self.height = h.max(320.0);
        self.inset_t = inset_t;
        self.inset_b = inset_b;
    }

    pub fn set_search(&mut self, text: String) {
        self.search = text;
        self.search_library(true);
    }

    pub fn set_closed(&mut self) {
        self.connected = false;
    }

    pub fn set_theme(&mut self, light: bool) {
        self.light_theme = light;
    }

    pub fn load_demo(&mut self) {
        self.connected = true;
        self.role = "controller".into();
        self.library = (0..48)
            .map(|i| LibrarySong {
                beatmap_id: format!("{i:032x}"),
                title: format!("Snow Crystal {i:02}"),
                artist: ["xi", "Camellia", "Sakuzyo", "t+pazolite"][i % 4].into(),
                difficulty_name: Some(["Easy", "Normal", "Hard", "Insane", "Expert"][i % 5].into()),
                creator: Some("mapper".into()),
                length_ms: 178_000.0 + i as f64 * 1400.0,
            })
            .collect();
        self.library_offset = self.library.len();
        self.library_has_more = false;
        self.queue = (0..8)
            .map(|i| QueueEntry {
                id: format!("q{i:031x}"),
                title: format!("Reserved {i}"),
                artist: "Artist".into(),
                difficulty_name: Some("Hard".into()),
                length_ms: 200_000.0,
                speed: if i == 1 { 1.1 } else { 1.0 },
                transpose: if i == 2 { 2 } else { 0 },
                mods: if i == 3 {
                    vec!["NF".into(), "IQ".into()]
                } else {
                    Vec::new()
                },
            })
            .collect();
        self.snapshot.song_title = Some("Cry For Me, Cry For You".into());
        self.snapshot.song_artist = Some("Jang Na-ra".into());
        self.snapshot.song_difficulty = Some("Hard".into());
        self.snapshot.current_lyrics = "지금 이 순간을".into();
        self.snapshot.next_lyrics = Some("놓치고 싶지 않아".into());
        self.snapshot.song_time = 42_000.0;
        self.snapshot.song_length = 246_000.0;
        self.snapshot.auto_advance_enabled = true;
        self.snapshot.notice = None;
        self.snapshot.paused = false;
        self.snapshot.speed = 1.0;
        log("ui.demo populated");
    }

    pub fn on_bytes(&mut self, bytes: &[u8]) {
        if let Some(msg) = parse_bin(bytes) {
            match msg.kind {
                KIND_WELCOME | KIND_RESUMED => {
                    self.connected = true;
                    if let (Some(id), Some(secret)) = (msg.session_id, msg.session_secret) {
                        store_session(&id, &secret);
                    }
                    if let Some(role) = msg.role {
                        self.role = role.to_ascii_lowercase();
                    }
                    if let Some(snapshot) = msg.snapshot {
                        self.apply_snapshot(snapshot);
                    }
                    log(&format!(
                        "ui.recv {} role={} gameplay={} queue={}",
                        if msg.kind == KIND_WELCOME {
                            "welcome"
                        } else {
                            "resumed"
                        },
                        self.role,
                        self.snapshot.has_gameplay(),
                        self.queue.len()
                    ));
                    self.trace(
                        "ui.wire",
                        "togglePlayback,seek,speed,transpose,previousPhrase,nextPhrase,retryPhrase,skipCurrent,skipToNext,librarySearch,queueAdd,queueAddNext,queuePlayNow,queueRemove,queueMove,queueClear,queueConfigure,autoAdvance,bgmVolume,vocalsVolume,monitorVolume,microphoneLatency,accompanimentLatency,lyricsLatency,setLoopA,setLoopB,clearLoop,loopPhrase,octaveFold,originalVocals,disconnect",
                    );
                    self.search_library(true);
                }
                KIND_STATE => {
                    self.connected = true;
                    if let Some(snapshot) = msg.snapshot {
                        self.apply_snapshot(snapshot);
                    }
                }
                KIND_QUEUE => {
                    if self.reorder_from.is_none() {
                        if let Some(entries) = msg.entries {
                            log(&format!("ui.recv queue n={}", entries.len()));
                            self.queue = entries;
                        }
                    }
                    if let Some(auto) = msg.auto_advance {
                        self.snapshot.auto_advance_enabled = auto;
                    }
                    self.maybe_play_after_add();
                }
                KIND_RESULT => {
                    if let Some(library) = msg.library {
                        self.ingest_library(library);
                    }
                    if let Some((id, success)) = self.pending.take() {
                        if msg.request_id.as_deref() == Some(id.as_str()) {
                            if msg.accepted.unwrap_or(false) {
                                if success != "moved" {
                                    self.toast(t(self.lang, success), false);
                                }
                                if self.play_after_add {
                                    self.maybe_play_after_add();
                                }
                            } else {
                                let error = msg.error.as_deref().unwrap_or("");
                                log(&format!("ui.result fail {error}"));
                                self.toast_error(error);
                                self.play_after_add = false;
                            }
                        } else {
                            self.pending = Some((id, success));
                        }
                    } else if msg.accepted == Some(false) {
                        self.toast_error(msg.error.as_deref().unwrap_or(""));
                    }
                }
                KIND_ERROR => {
                    let error = msg.error.as_deref().unwrap_or(t(self.lang, "failed"));
                    log(&format!("ui.recv error {error}"));
                    self.toast_error(error);
                }
                _ => {}
            }
            return;
        }

        let text = String::from_utf8_lossy(bytes);
        let Some(msg) = parse_envelope(&text) else {
            log("ui.recv unparsed");
            return;
        };
        match msg.kind.as_str() {
            "welcome" | "resumed" => {
                self.connected = true;
                if let Some(role) = msg.role {
                    self.role = role.to_ascii_lowercase();
                }
                if let Some(snapshot) = msg.snapshot {
                    self.apply_snapshot(snapshot);
                }
                self.search_library(true);
            }
            "state" => {
                self.connected = true;
                if let Some(snapshot) = msg.snapshot {
                    self.apply_snapshot(snapshot);
                }
            }
            "queue" => {
                if self.reorder_from.is_none() {
                    if let Some(entries) = msg.entries {
                        self.queue = entries;
                    }
                }
                if let Some(auto) = msg.auto_advance_enabled {
                    self.snapshot.auto_advance_enabled = auto;
                }
                self.maybe_play_after_add();
            }
            "commandResult" => {
                if let Some(library) = msg.library {
                    self.library = library;
                }
                if let Some((id, success)) = self.pending.take() {
                    if msg.request_id.as_deref() == Some(id.as_str()) {
                        if msg.accepted.unwrap_or(false) {
                            if success != "moved" {
                                self.toast(t(self.lang, success), false);
                            }
                            if self.play_after_add {
                                self.maybe_play_after_add();
                            }
                        } else {
                            self.toast_error(msg.error.as_deref().unwrap_or(""));
                            self.play_after_add = false;
                        }
                    } else {
                        self.pending = Some((id, success));
                    }
                } else if msg.accepted == Some(false) {
                    self.toast_error(msg.error.as_deref().unwrap_or(""));
                }
            }
            "error" => {
                self.toast_error(msg.error.as_deref().unwrap_or(""));
            }
            _ => {}
        }
    }

    fn apply_snapshot(&mut self, snapshot: Snapshot) {
        if snapshot.queue.is_empty() && !self.queue.is_empty() && snapshot.notice.is_some() {
        } else if !snapshot.queue.is_empty() && self.reorder_from.is_none() {
            self.queue = snapshot.queue.clone();
        }
        self.snapshot = snapshot;
        if !self.snapshot.queue.is_empty() && self.reorder_from.is_none() {
            self.queue = self.snapshot.queue.clone();
        }
    }

    fn maybe_play_after_add(&mut self) {
        if !self.play_after_add || self.queue.is_empty() || !self.controller() {
            return;
        }
        if !self.can_navigate() {
            return;
        }
        self.play_after_add = false;
        let entry = if self.add_next {
            self.queue.first()
        } else {
            self.queue.last()
        };
        if let Some(entry) = entry {
            let id = entry.id.clone();
            self.request("queuePlayNow", None, None, Some(&id), None, "playNow");
        }
    }

    pub fn pointer(&mut self, kind: i32, x: f32, y: f32) {
        const TAP_SLOP: f32 = 28.0;
        const PAGER_SLOP: f32 = 48.0;
        match kind {
            0 => {
                self.pointer_down = true;
                self.px = x;
                self.py = y;
                self.sx = x;
                self.sy = y;
                self.start_page = self.page;
                let page = self.current_page() as usize;
                self.start_scroll = self.scroll[page];
                self.scroll_vel[page] = 0.0;
                self.vel_samples.clear();
                self.push_vel(y);
                self.axis = Axis::None;
                self.drag = DragKind::None;
                self.press_action = self.hit_at(x, y);
                self.skip_hold_started =
                    matches!(self.press_action, Some(Action::Skip)).then_some(self.now);
                if let Some(hit) = &self.press_action {
                    match hit {
                        Action::SeekTo(_) => self.drag = DragKind::Seek,
                        Action::Mixer(_, _) | Action::Latency(_, _) | Action::SheetSpeed(_) => {
                            self.drag = DragKind::Slider
                        }
                        Action::QueueGrab(index) => {
                            self.drag = DragKind::Reorder;
                            self.reorder_from = Some(*index);
                            self.reorder_origin_y = y;
                            self.reorder_finger_y = y;
                            self.reorder_list_y = self.queue_list_top();
                        }
                        _ => {}
                    }
                }
            }
            1 if self.pointer_down => {
                let dx = x - self.sx;
                let dy = y - self.sy;
                if matches!(self.drag, DragKind::None)
                    && self.sheet.is_none()
                    && self.confirm.is_none()
                    && self.axis_free()
                {
                    if dx.abs() >= PAGER_SLOP && dx.abs() > dy.abs() * 1.2 {
                        self.axis = Axis::Horiz;
                        self.drag = DragKind::Pager;
                        self.press_action = None;
                    } else if dy.abs() >= TAP_SLOP && dy.abs() > dx.abs() {
                        self.axis = Axis::Vert;
                        self.drag = DragKind::Scroll;
                        self.press_action = None;
                    }
                }
                match self.drag {
                    DragKind::Pager => {
                        let next =
                            (self.start_page - dx / self.width).clamp(0.0, (PAGE_COUNT - 1) as f32);
                        self.page = next;
                    }
                    DragKind::Scroll => {
                        self.push_vel(y);
                        let idx = self.current_page() as usize;
                        self.scroll[idx] = self.start_scroll - (y - self.sy);
                    }
                    DragKind::Seek => {
                        if self.now - self.last_seek_sent > 80.0 {
                            self.last_seek_sent = self.now;
                            self.cmd("seek", Some(self.seek_from_x(x)), None, None);
                        }
                    }
                    DragKind::Slider => {
                        // Mixer/latency sliders sit on the Info page where vertical
                        // scrolling is common. Do not alter a setting until this is a
                        // deliberate horizontal drag rather than a landing tap.
                        if dx.abs() >= 8.0 {
                            if let Some(action) = self.hit_at(x, y) {
                                self.fire(action);
                            }
                        }
                    }
                    DragKind::Reorder => {
                        self.reorder_finger_y = y;
                    }
                    DragKind::None => {}
                }
                self.px = x;
                self.py = y;
            }
            2 | 3 => {
                if self.pointer_down {
                    let dx = x - self.sx;
                    let dy = y - self.sy;
                    match self.drag {
                        DragKind::Pager => {
                            let threshold = self.width * 0.18;
                            if dx < -threshold {
                                self.target =
                                    (self.start_page.round() as i32 + 1).min(PAGE_COUNT - 1);
                            } else if dx > threshold {
                                self.target = (self.start_page.round() as i32 - 1).max(0);
                            } else {
                                self.target = self.page.round().clamp(0.0, 3.0) as i32;
                            }
                        }
                        DragKind::Scroll => {
                            self.push_vel(y);
                            let vel = self.release_velocity();
                            let page = self.current_page() as usize;
                            self.scroll_vel[page] = if vel.abs() > 120.0 { vel } else { 0.0 };
                        }
                        DragKind::Seek => {
                            if let Some(action) = self.hit_at(x, y) {
                                self.fire(action);
                            }
                        }
                        DragKind::Slider => {
                            if dx.abs() >= 8.0 {
                                if let Some(action) = self.hit_at(x, y) {
                                    self.fire(action);
                                }
                            }
                        }
                        DragKind::Reorder => {
                            self.finish_reorder();
                        }
                        DragKind::None => {
                            if dx.abs() < TAP_SLOP && dy.abs() < TAP_SLOP {
                                if let Some(action) = self.press_action.take() {
                                    if matches!(action, Action::Skip) {
                                        if self
                                            .skip_hold_started
                                            .is_some_and(|start| self.now - start >= 700.0)
                                        {
                                            self.request(
                                                "skipCurrent",
                                                None,
                                                None,
                                                None,
                                                None,
                                                "skip",
                                            );
                                        } else {
                                            self.toast("Hold End song to confirm", false);
                                        }
                                    } else {
                                        self.fire(action);
                                    }
                                } else {
                                    self.dismiss_back();
                                }
                            }
                        }
                    }
                }
                self.pointer_down = false;
                self.drag = DragKind::None;
                self.axis = Axis::None;
                self.press_action = None;
                self.skip_hold_started = None;
            }
            _ => {}
        }
    }

    pub fn wheel(&mut self, _x: f32, _y: f32, dy: f32) {
        if self.sheet.is_some() || self.confirm.is_some() || self.reorder_from.is_some() {
            return;
        }
        let idx = self.current_page() as usize;
        self.scroll[idx] += dy;
        if !self.reduced {
            self.scroll_vel[idx] = (self.scroll_vel[idx] + dy * 8.0).clamp(-4200.0, 4200.0);
        }
    }

    fn axis_free(&self) -> bool {
        !matches!(
            self.drag,
            DragKind::Seek | DragKind::Slider | DragKind::Reorder
        )
    }

    fn current_page(&self) -> i32 {
        self.page.round().clamp(0.0, 3.0) as i32
    }

    fn controller(&self) -> bool {
        self.role != "spectator"
    }

    fn content_top(&self) -> f32 {
        HEADER_H + self.inset_t
    }

    fn content_bottom(&self) -> f32 {
        // The global now-playing dock owns pause/resume on every page. Reserve its full
        // footprint so page content and iOS home indicators never sit underneath it.
        72.0 + self.inset_b
    }

    fn queue_list_top(&self) -> f32 {
        self.content_top() + 56.0
    }

    fn seek_from_x(&self, x: f32) -> f64 {
        let pad = 16.0;
        let left = pad + 44.0;
        let right = self.width - pad - 44.0;
        let t = ((x - left) / (right - left)).clamp(0.0, 1.0) as f64;
        t * self.snapshot.song_length.max(1.0)
    }

    fn hit_at(&self, x: f32, y: f32) -> Option<Action> {
        self.hits.iter().rev().find_map(|hit| {
            if x >= hit.x && y >= hit.y && x <= hit.x + hit.w && y <= hit.y + hit.h {
                Some(hit.action.clone())
            } else {
                None
            }
        })
    }

    fn push_vel(&mut self, y: f32) {
        self.vel_samples.push((self.now, y));
        if self.vel_samples.len() > 8 {
            self.vel_samples.remove(0);
        }
    }

    fn release_velocity(&self) -> f32 {
        let now = self.now;
        let Some(&(t1, y1)) = self.vel_samples.last() else {
            return 0.0;
        };
        let mut t0 = t1;
        let mut y0 = y1;
        for &(t, y) in &self.vel_samples {
            if now - t <= 100.0 {
                t0 = t;
                y0 = y;
                break;
            }
        }
        let dt = (t1 - t0).max(8.0);
        ((y0 - y1) / dt as f32) * 1000.0
    }

    fn display_scroll(&self, page: i32) -> f32 {
        let s = self.scroll[page as usize];
        let max = self.scroll_max[page as usize];
        if self.reduced {
            return s.clamp(0.0, max);
        }
        if s < 0.0 {
            s * 0.48
        } else if s > max {
            max + (s - max) * 0.48
        } else {
            s
        }
    }

    fn step_inertia(&mut self, now: f64) {
        let dt = if self.last_frame <= 0.0 {
            16.0
        } else {
            (now - self.last_frame).clamp(8.0, 40.0)
        };
        self.last_frame = now;
        if self.pointer_down || self.reorder_from.is_some() {
            return;
        }
        let page = self.current_page() as usize;
        let max = self.scroll_max[page];
        let mut s = self.scroll[page];
        let mut v = self.scroll_vel[page];
        if self.reduced {
            self.scroll[page] = s.clamp(0.0, max);
            self.scroll_vel[page] = 0.0;
            return;
        }
        if v.abs() > 18.0 {
            s += v * dt as f32 / 1000.0;
            let mut decay = 0.998_f32.powf(dt as f32);
            if s < 0.0 || s > max {
                decay *= 0.90;
            }
            v *= decay;
            if v.abs() < 18.0 {
                v = 0.0;
            }
        }
        if v.abs() < 40.0 {
            if s < 0.0 {
                s += (0.0 - s) * (1.0 - 0.86_f32.powf(dt as f32 / 16.0));
                if s > -0.5 {
                    s = 0.0;
                }
                v = 0.0;
            } else if s > max {
                s += (max - s) * (1.0 - 0.86_f32.powf(dt as f32 / 16.0));
                if s < max + 0.5 {
                    s = max;
                }
                v = 0.0;
            }
        }
        self.scroll[page] = s;
        self.scroll_vel[page] = v;
    }

    fn reorder_target(&self) -> usize {
        if self.queue.is_empty() {
            return 0;
        }
        let scroll = self.display_scroll(PAGE_QUEUE);
        let center = self.reorder_finger_y - self.reorder_list_y + scroll;
        let last = self.queue.len() - 1;
        let idx = (center / QUEUE_ROW).floor() as i32;
        idx.clamp(0, last as i32) as usize
    }

    fn finish_reorder(&mut self) {
        let Some(from) = self.reorder_from.take() else {
            return;
        };
        if from >= self.queue.len() {
            return;
        }
        let to = self.reorder_target().min(self.queue.len() - 1);
        if to == from {
            return;
        }
        let id = self.queue[from].id.clone();
        let entry = self.queue.remove(from);
        self.queue.insert(to, entry);
        self.request("queueMove", Some(to as f64), None, Some(&id), None, "moved");
    }

    fn fire(&mut self, action: Action) {
        log(&format!("ui.fire {action:?}"));
        self.trace("ui.fire", &format!("{action:?}"));
        match action {
            Action::PlayPause => self.cmd("togglePlayback", None, None, None),
            Action::Restart => {
                self.cmd("seek", Some(0.0), None, None);
                self.cmd("play", None, None, None);
            }
            Action::Skip => self.confirm = Some(Confirm::Skip),
            Action::NextSong => {
                if !self.can_navigate() {
                    return;
                }
                self.request("skipToNext", None, None, None, None, "playNow");
            }
            Action::PrevPhrase => self.cmd("previousPhrase", None, None, None),
            Action::Retry => self.cmd("retryPhrase", None, None, None),
            Action::NextPhrase => self.cmd("nextPhrase", None, None, None),
            Action::KeyDelta(delta) => {
                let next = (self.snapshot.mixer.transpose + delta).clamp(-6, 6);
                self.cmd("transpose", Some(next as f64), None, None);
            }
            Action::KeyReset => self.cmd("transpose", Some(0.0), None, None),
            Action::SpeedDelta(delta) => {
                let next = (self.snapshot.speed + delta).clamp(0.5, 1.5);
                self.cmd("speed", Some((next * 20.0).round() / 20.0), None, None);
            }
            Action::SpeedReset => self.cmd("speed", Some(1.0), None, None),
            Action::SeekTo(time) => self.cmd("seek", Some(time), None, None),
            Action::OpenSong(id) => {
                self.search_open = false;
                if let Some(song) = self.library.iter().find(|song| song.beatmap_id == id) {
                    self.sheet = Some(Sheet {
                        kind: SheetKind::Add,
                        id,
                        title: song.title.clone(),
                        artist: song.artist.clone(),
                        speed: 1.0,
                        transpose: 0,
                        mods: default_mods_from(&self.snapshot.active_mods),
                    });
                }
            }
            Action::CloseSheet | Action::Back => {
                self.dismiss_back();
            }
            Action::QueueAdd => self.submit_sheet(false, false),
            Action::QueueAddNext => self.submit_sheet(true, false),
            Action::PlayNow => self.submit_sheet(false, true),
            Action::ToggleSearch => {
                self.search_open = !self.search_open;
                if !self.search_open {
                    self.search_box = (0.0, 0.0, 0.0, 0.0);
                }
            }
            Action::RememberDevice => {
                self.remember = !self.remember;
                set_remembered(self.remember);
            }
            Action::QueueMenu => {
                self.queue_menu = !self.queue_menu;
            }
            Action::ToggleMod(acronym) => {
                if let Some(sheet) = &mut self.sheet {
                    if let Some(mod_slot) = sheet.mods.iter_mut().find(|item| item.0 == acronym) {
                        mod_slot.2 = !mod_slot.2;
                    }
                }
            }
            Action::SheetKey(delta) => {
                if let Some(sheet) = &mut self.sheet {
                    if delta == 0 {
                        sheet.transpose = 0;
                    } else {
                        sheet.transpose = (sheet.transpose + delta).clamp(-6, 6);
                    }
                }
            }
            Action::SheetSpeed(delta) => {
                if let Some(sheet) = &mut self.sheet {
                    if delta == 0.0 {
                        sheet.speed = 1.0;
                    } else {
                        sheet.speed = ((sheet.speed + delta) * 20.0).round() / 20.0;
                        sheet.speed = sheet.speed.clamp(0.5, 1.5);
                    }
                }
            }
            Action::QueuePlay(id) => {
                if !self.can_navigate() {
                    return;
                }
                self.sheet = None;
                self.request("queuePlayNow", None, None, Some(&id), None, "playNow");
            }
            Action::QueueRemove(id) => {
                self.sheet = None;
                self.request("queueRemove", None, None, Some(&id), None, "removed");
            }
            Action::QueueGrab(_) => {}
            Action::QueueClear => self.confirm = Some(Confirm::Clear),
            Action::QueueEdit(index) => {
                if let Some(entry) = self.queue.get(index) {
                    let mut mods = default_mods();
                    for item in &mut mods {
                        item.2 = entry.mods.iter().any(|mod_name| mod_name == &item.0);
                    }
                    self.sheet = Some(Sheet {
                        kind: SheetKind::Edit,
                        id: entry.id.clone(),
                        title: entry.title.clone(),
                        artist: entry.artist.clone(),
                        speed: if entry.speed <= 0.0 { 1.0 } else { entry.speed },
                        transpose: entry.transpose,
                        mods,
                    });
                }
            }
            Action::AutoAdvance => {
                let next = !self.snapshot.auto_advance_enabled;
                self.cmd("autoAdvance", None, Some(next), None);
                self.snapshot.auto_advance_enabled = next;
            }
            Action::Mixer(name, value) => self.cmd(name, Some(value), None, None),
            Action::Latency(name, value) => self.cmd(name, Some(value), None, None),
            Action::LoopA => self.cmd("setLoopA", None, None, None),
            Action::LoopB => self.cmd("setLoopB", None, None, None),
            Action::ClearLoop => self.cmd("clearLoop", None, None, None),
            Action::LoopPhrase => {
                let next = !self.snapshot.r#loop().current_phrase;
                self.cmd("loopPhrase", None, Some(next), None);
            }
            Action::Octave => {
                self.cmd(
                    "octaveFold",
                    None,
                    Some(!self.snapshot.mixer.octave_fold),
                    None,
                );
            }
            Action::Vocals => {
                self.cmd(
                    "originalVocals",
                    None,
                    Some(!self.snapshot.mixer.original_vocals_enabled),
                    None,
                );
            }
            Action::Language => self.lang = self.lang.next(),
            Action::InfoAudio => self.info_view = InfoView::Audio,
            Action::InfoAppearance => self.info_view = InfoView::Appearance,
            Action::InfoBack => self.info_view = InfoView::Overview,
            Action::Theme(light) => {
                self.light_theme = light;
                persist_theme(light);
            }
            Action::Disconnect => self.cmd("disconnect", None, None, None),
            Action::ConfirmYes => match self.confirm.take() {
                Some(Confirm::Skip) => self.request("skipCurrent", None, None, None, None, "skip"),
                Some(Confirm::Clear) => self.request("queueClear", None, None, None, None, "clear"),
                None => {}
            },
            Action::ConfirmNo => self.confirm = None,
            Action::Page(page) => {
                self.target = page.clamp(0, PAGE_COUNT - 1);
                if self.target != PAGE_LIST {
                    self.search_open = false;
                }
                if self.target != PAGE_QUEUE {
                    self.queue_menu = false;
                }
                if self.target != PAGE_INFO {
                    self.info_view = InfoView::Overview;
                }
            }
        }
    }

    fn submit_sheet(&mut self, add_next: bool, play_now: bool) {
        let Some(sheet) = self.sheet.take() else {
            return;
        };
        let mods: Vec<String> = sheet
            .mods
            .iter()
            .filter(|item| item.2)
            .map(|item| item.0.clone())
            .collect();
        let options = Some((sheet.speed, sheet.transpose, mods));
        match sheet.kind {
            SheetKind::Add => {
                if play_now {
                    self.play_after_add = false;
                    self.request(
                        "queuePlayNow",
                        None,
                        None,
                        Some(&sheet.id),
                        options,
                        "playNow",
                    );
                    return;
                }
                self.play_after_add = false;
                self.add_next = add_next;
                let name = if add_next { "queueAddNext" } else { "queueAdd" };
                self.request(name, None, None, Some(&sheet.id), options, "queued");
            }
            SheetKind::Edit => {
                self.request(
                    "queueConfigure",
                    None,
                    None,
                    Some(&sheet.id),
                    options,
                    "saved",
                );
            }
        }
    }

    fn search_library(&mut self, reset: bool) {
        if reset {
            self.library.clear();
            self.library_offset = 0;
            self.library_has_more = true;
            self.scroll[0] = 0.0;
            self.scroll_vel[0] = 0.0;
        }
        if self.library_loading || !self.library_has_more {
            return;
        }
        self.library_loading = true;
        let id = format!("library-{}", self.request);
        self.request += 1;
        self.cmd_full(
            "librarySearch",
            Some(self.library_offset as f64),
            None,
            Some(&self.search.clone()),
            None,
            Some(&id),
        );
    }

    fn ingest_library(&mut self, page: Vec<LibrarySong>) {
        const PAGE: usize = 80;
        log(&format!(
            "ui.recv library page={} offset={}",
            page.len(),
            self.library_offset
        ));
        self.library_loading = false;
        self.library_has_more = page.len() >= PAGE;
        if self.library_offset == 0 {
            self.library = page;
        } else {
            for song in page {
                if !self
                    .library
                    .iter()
                    .any(|existing| existing.beatmap_id == song.beatmap_id)
                {
                    self.library.push(song);
                }
            }
        }
        self.library_offset = self.library.len();
    }

    fn cmd(&mut self, name: &str, value: Option<f64>, enabled: Option<bool>, text: Option<&str>) {
        self.cmd_full(name, value, enabled, text, None, None);
    }

    fn request(
        &mut self,
        name: &str,
        value: Option<f64>,
        enabled: Option<bool>,
        text: Option<&str>,
        options: Option<(f64, i32, Vec<String>)>,
        success: &'static str,
    ) {
        let id = format!("action-{}", self.request);
        self.request += 1;
        self.pending = Some((id.clone(), success));
        self.cmd_full(name, value, enabled, text, options, Some(&id));
    }

    fn cmd_full(
        &mut self,
        name: &str,
        value: Option<f64>,
        enabled: Option<bool>,
        text: Option<&str>,
        options: Option<(f64, i32, Vec<String>)>,
        request_id: Option<&str>,
    ) {
        if !self.connected && name != "disconnect" {
            log(&format!("ui.drop {name} disconnected"));
            self.toast(t(self.lang, "closed"), true);
            return;
        }
        self.sequence = self.sequence.saturating_add(1).max(1);
        store_seq(self.sequence);
        log(&format!(
            "ui.send {name} seq={} value={value:?} enabled={enabled:?} text={text:?}",
            self.sequence
        ));
        self.trace("ui.send", name);
        let packed = options
            .as_ref()
            .map(|(speed, transpose, mods)| (*speed, *transpose, mods.as_slice()));
        send(&encode_command(
            self.sequence,
            name,
            value,
            enabled,
            text,
            request_id,
            packed,
        ));
    }

    fn trace(&mut self, event: &str, detail: &str) {
        send(&encode_trace(event, detail));
    }

    fn toast(&mut self, text: &str, error: bool) {
        self.toast = Some(Toast {
            text: text.into(),
            error,
            until: self.now + 2200.0,
        });
    }

    fn dismiss_back(&mut self) -> bool {
        if self.confirm.is_some() {
            self.confirm = None;
            return true;
        }
        if self.sheet.is_some() {
            self.sheet = None;
            return true;
        }
        if self.queue_menu {
            self.queue_menu = false;
            return true;
        }
        if self.info_view != InfoView::Overview {
            self.info_view = InfoView::Overview;
            return true;
        }
        if self.search_open {
            self.search_open = false;
            return true;
        }
        if self.target != PAGE_CONTROL {
            self.target = PAGE_CONTROL;
            return true;
        }
        false
    }

    fn can_navigate(&mut self) -> bool {
        if self.now - self.last_nav < 800.0 {
            return false;
        }
        self.last_nav = self.now;
        true
    }

    fn toast_error(&mut self, error: &str) {
        if silent_error(error) {
            log(&format!("ui.ignore {error}"));
            return;
        }
        self.toast(friendly_error(self.lang, error), true);
    }

    pub fn frame(&mut self, now: f64) {
        self.now = now;
        self.step_inertia(now);
        if !self.pointer_down || !matches!(self.drag, DragKind::Pager) {
            let target = self.target as f32;
            if self.reduced {
                self.page = target;
            } else {
                self.page += (target - self.page) * 0.28;
                if (self.page - target).abs() < 0.004 {
                    self.page = target;
                }
            }
        }
        if self.toast.as_ref().is_some_and(|toast| now > toast.until) {
            self.toast = None;
        }
        self.hits.clear();
        fill_rect(0.0, 0.0, self.width, self.height, BG, 0.0);
        self.draw_pages();
        self.draw_chrome();
        self.draw_now_playing_dock();
        if let Some(sheet) = self.sheet.as_ref() {
            let title = sheet.title.clone();
            let artist = sheet.artist.clone();
            let kind = sheet.kind;
            let id = sheet.id.clone();
            let speed = sheet.speed;
            let transpose = sheet.transpose;
            let mods = sheet.mods.clone();
            self.draw_sheet(kind, &id, &title, &artist, speed, transpose, &mods);
        }
        if let Some(confirm) = self.confirm {
            self.draw_confirm(confirm);
        }
        if let Some(toast) = &self.toast {
            let color = if toast.error { DANGER } else { PINK };
            let w = (measure(14.0, 1, &toast.text) + 36.0).min(self.width - 24.0);
            fill_rect(
                (self.width - w) * 0.5,
                self.height - 76.0 - self.inset_b,
                w,
                38.0,
                PANEL2,
                6.0,
            );
            stroke_rect(
                (self.width - w) * 0.5,
                self.height - 76.0 - self.inset_b,
                w,
                38.0,
                color,
                6.0,
                1.0,
            );
            fill_text(
                self.width * 0.5,
                self.height - 57.0 - self.inset_b,
                14.0,
                color,
                1,
                1,
                &toast.text,
            );
        }
        let show_search = self.search_open
            && self.target == PAGE_LIST
            && self.sheet.is_none()
            && self.confirm.is_none();
        let (x, y, w, h) = self.search_box;
        search_rect(x, y, w, h, show_search);
    }

    fn draw_now_playing_dock(&mut self) {
        let h = 64.0 + self.inset_b;
        let y = self.height - h;
        fill_rect(0.0, y, self.width, h, HEADER, 0.0);
        fill_rect(0.0, y, self.width, 1.0, LINE, 0.0);
        let pad = 14.0;
        let title = self
            .snapshot
            .song_title
            .as_deref()
            .unwrap_or_else(|| t(self.lang, "idle"));
        let artist = self.snapshot.song_artist.as_deref().unwrap_or("—");
        let controls_w = 88.0;
        fill_text(
            pad,
            y + 21.0,
            13.0,
            TEXT,
            0,
            1,
            &ellipsize(13.0, 1, title, self.width - pad * 2.0 - controls_w),
        );
        fill_text(
            pad,
            y + 42.0,
            10.0,
            MUTED,
            0,
            0,
            &ellipsize(10.0, 0, artist, self.width - pad * 2.0 - controls_w),
        );
        let live = self.snapshot.has_gameplay();
        let pause = if self.snapshot.paused {
            t(self.lang, "play")
        } else {
            t(self.lang, "pause")
        };
        self.lazer_button(
            self.width - pad - controls_w,
            y + 12.0,
            42.0,
            38.0,
            pause,
            Action::PlayPause,
            live,
            false,
        );
        self.lazer_button(
            self.width - pad - 40.0,
            y + 12.0,
            40.0,
            38.0,
            "››",
            Action::NextSong,
            live,
            true,
        );
    }

    fn draw_chrome(&mut self) {
        let h = HEADER_H + self.inset_t;
        fill_rect(0.0, 0.0, self.width, h, HEADER, 0.0);
        fill_rect(0.0, h - 1.0, self.width, 1.0, LINE, 0.0);
        let cy = 18.0 + self.inset_t;
        fill_text(12.0, cy, 15.0, PINK, 0, 1, "uta!");
        let brand_w = measure(15.0, 1, "uta!");
        fill_rect(
            12.0 + brand_w + 6.0,
            cy - 4.0,
            7.0,
            7.0,
            if self.connected { OK } else { DANGER },
            4.0,
        );

        let labels = ["list", "control", "queue", "info"];
        let nav_w = 196.0;
        let nav_x = (self.width - nav_w) * 0.5;
        let tw = nav_w / 4.0;
        for i in 0..4 {
            let x = nav_x + tw * i as f32;
            let active = self.target == i;
            fill_text(
                x + tw * 0.5,
                cy,
                11.0,
                if active { PINK } else { MUTED },
                1,
                if active { 1 } else { 0 },
                t(self.lang, labels[i as usize]),
            );
            self.hit(x, 4.0 + self.inset_t, tw, 28.0, Action::Page(i));
        }

        let page = self.current_page();
        if page == PAGE_LIST {
            fill_text(
                self.width - 12.0,
                cy,
                11.0,
                if self.search_open { PINK } else { TEXT },
                2,
                1,
                t(self.lang, "searchBtn"),
            );
            self.hit(
                self.width - 64.0,
                4.0 + self.inset_t,
                56.0,
                28.0,
                Action::ToggleSearch,
            );
        } else if page == PAGE_QUEUE && self.controller() {
            fill_text(
                self.width - 14.0,
                cy,
                16.0,
                if self.queue_menu { PINK } else { TEXT },
                2,
                1,
                "···",
            );
            self.hit(
                self.width - 44.0,
                4.0 + self.inset_t,
                40.0,
                28.0,
                Action::QueueMenu,
            );
        }

        if self.queue_menu && page == PAGE_QUEUE && self.controller() {
            let mw = 132.0;
            let mh = 72.0;
            let mx = self.width - mw - 8.0;
            let my = h + 4.0;
            fill_rect(mx, my, mw, mh, PANEL, 8.0);
            stroke_rect(mx, my, mw, mh, LINE, 8.0, 1.0);
            let auto = if self.snapshot.auto_advance_enabled {
                PINK
            } else {
                TEXT
            };
            fill_text(
                mx + mw * 0.5,
                my + 22.0,
                13.0,
                auto,
                1,
                1,
                t(self.lang, "auto"),
            );
            self.hit(mx, my, mw, 36.0, Action::AutoAdvance);
            fill_text(
                mx + mw * 0.5,
                my + 52.0,
                13.0,
                DANGER,
                1,
                1,
                t(self.lang, "clear"),
            );
            self.hit(mx, my + 36.0, mw, 36.0, Action::QueueClear);
        }
    }

    fn draw_pages(&mut self) {
        let top = self.content_top();
        let bottom = self.content_bottom();
        let h = self.height - top - bottom;
        clip(0.0, top, self.width, h);
        for i in 0..PAGE_COUNT {
            let x = (i as f32 - self.page) * self.width;
            if x > -self.width && x < self.width {
                match i {
                    PAGE_LIST => self.draw_list(x, top, h),
                    PAGE_CONTROL => self.draw_control(x, top, h),
                    PAGE_QUEUE => self.draw_queue(x, top, h),
                    _ => self.draw_info(x, top, h),
                }
            }
        }
        unclip();
    }

    fn draw_list(&mut self, x: f32, y: f32, h: f32) {
        let pad = 14.0;
        let mut list_y = y + 8.0;
        if self.search_open {
            self.search_box = (x + pad, y + 6.0, self.width - pad * 2.0, 36.0);
            fill_rect(
                self.search_box.0,
                self.search_box.1,
                self.search_box.2,
                self.search_box.3,
                0x6A000000,
                6.0,
            );
            list_y = y + 48.0;
        } else {
            self.search_box = (0.0, 0.0, 0.0, 0.0);
        }
        let list_h = h - (list_y - y) - 4.0;
        self.note_scroll_max(PAGE_LIST, self.library.len() as f32 * SONG_ROW - list_h);
        clip(x, list_y, self.width, list_h);
        if self.library.is_empty() {
            fill_text(
                x + self.width * 0.5,
                list_y + 80.0,
                14.0,
                MUTED,
                1,
                0,
                t(self.lang, "noSongs"),
            );
        } else {
            let scroll = self.display_scroll(PAGE_LIST);
            let first = (scroll / SONG_ROW).floor().max(0.0) as usize;
            let visible = (list_h / SONG_ROW).ceil() as usize + 2;
            let last = (first + visible).min(self.library.len());
            let songs = self.library[first..last].to_vec();
            for (offset, song) in songs.iter().enumerate() {
                let index = first + offset;
                let ry = list_y + index as f32 * SONG_ROW - scroll;
                self.draw_song_row(
                    x + pad,
                    ry + 4.0,
                    self.width - pad * 2.0,
                    SONG_ROW - 8.0,
                    song,
                    true,
                );
            }
            if self.library_has_more && last + 8 >= self.library.len() {
                self.search_library(false);
            }
        }
        unclip();
    }

    fn draw_song_row(&mut self, x: f32, y: f32, w: f32, h: f32, song: &LibrarySong, open: bool) {
        fill_rect(x, y, w, h, ROW, 8.0);
        let stripe = diff_color(song.difficulty_name.as_deref().unwrap_or(""));
        fill_rect(x, y, 4.0, h, stripe, 2.0);
        let title_w = w - 20.0;
        fill_text(
            x + 14.0,
            y + 20.0,
            16.0,
            TEXT,
            0,
            1,
            &ellipsize(16.0, 1, &song.title, title_w),
        );
        let meta = format!(
            "{}  ·  [{}]  ·  {}{}",
            empty(&song.artist),
            song.difficulty_name.as_deref().unwrap_or("—"),
            fmt_ms(song.length_ms),
            song.creator
                .as_deref()
                .filter(|c| !c.is_empty())
                .map(|c| format!("  ·  {c}"))
                .unwrap_or_default()
        );
        fill_text(
            x + 14.0,
            y + 42.0,
            11.0,
            MUTED,
            0,
            0,
            &ellipsize(11.0, 0, &meta, title_w),
        );
        if open {
            self.hit(x, y, w, h, Action::OpenSong(song.beatmap_id.clone()));
        }
    }

    fn has_practice(&self) -> bool {
        self.snapshot.active_mods.iter().any(|item| item == "PR")
    }

    fn draw_control(&mut self, x: f32, y: f32, h: f32) {
        let pad = 14.0;
        let live = self.snapshot.has_gameplay();
        let inner = self.width - pad * 2.0;
        let artist = self.snapshot.song_artist.as_deref().unwrap_or("");
        if !artist.is_empty() {
            fill_text(
                x + pad,
                y + 14.0,
                12.0,
                MUTED,
                0,
                0,
                &ellipsize(12.0, 0, artist, inner),
            );
        } else if !live {
            fill_text(x + pad, y + 14.0, 12.0, MUTED, 0, 0, t(self.lang, "idle"));
        }

        let lyrics = if live {
            if self.snapshot.current_lyrics.is_empty() {
                "—"
            } else {
                &self.snapshot.current_lyrics
            }
        } else {
            self.snapshot
                .notice
                .as_deref()
                .filter(|s| !s.is_empty())
                .unwrap_or("")
        };
        fill_text(x + self.width * 0.5, y + 44.0, 18.0, TEXT, 1, 1, lyrics);
        if let Some(next) = self.snapshot.next_lyrics.as_deref() {
            fill_text(x + self.width * 0.5, y + 68.0, 12.0, MUTED, 1, 0, next);
        }

        let seek_y = y + 86.0;
        fill_text(
            x + pad,
            seek_y + 10.0,
            11.0,
            MUTED,
            0,
            0,
            &fmt_ms(self.snapshot.song_time),
        );
        fill_text(
            x + self.width - pad,
            seek_y + 10.0,
            11.0,
            MUTED,
            2,
            0,
            &fmt_ms(self.snapshot.song_length),
        );
        let sl = x + pad + 40.0;
        let sw = inner - 80.0;
        fill_rect(sl, seek_y + 6.0, sw, 5.0, LINE, 3.0);
        let tpos = if self.snapshot.song_length > 0.0 {
            (self.snapshot.song_time / self.snapshot.song_length) as f32
        } else {
            0.0
        };
        fill_rect(sl, seek_y + 6.0, sw * tpos.clamp(0.0, 1.0), 5.0, PINK, 3.0);
        fill_rect(
            sl + sw * tpos.clamp(0.0, 1.0) - 5.0,
            seek_y + 3.0,
            10.0,
            12.0,
            TEXT,
            5.0,
        );
        if live && self.controller() {
            self.hit(
                sl - 8.0,
                seek_y - 6.0,
                sw + 16.0,
                28.0,
                Action::SeekTo(self.snapshot.song_time),
            );
        }

        let col = (inner - 10.0) / 3.0;
        let key = format!("{:+}", self.snapshot.mixer.transpose);
        let speed = format!("{}%", (self.snapshot.speed * 100.0).round());
        let half = (inner - 8.0) * 0.5;
        let step_y = seek_y + 28.0;
        self.compact_stepper(
            x + pad,
            step_y,
            half,
            t(self.lang, "key"),
            &key,
            Action::KeyDelta(-1),
            Action::KeyReset,
            Action::KeyDelta(1),
            live,
        );
        self.compact_stepper(
            x + pad + half + 8.0,
            step_y,
            half,
            t(self.lang, "std"),
            &speed,
            Action::SpeedDelta(-0.05),
            Action::SpeedReset,
            Action::SpeedDelta(0.05),
            live,
        );

        let mut yy = step_y + 44.0;
        if self.has_practice() {
            self.lazer_button(
                x + pad,
                yy,
                col,
                36.0,
                t(self.lang, "prev"),
                Action::PrevPhrase,
                live,
                false,
            );
            self.lazer_button(
                x + pad + col + 5.0,
                yy,
                col,
                36.0,
                t(self.lang, "retry"),
                Action::Retry,
                live,
                false,
            );
            self.lazer_button(
                x + pad + (col + 5.0) * 2.0,
                yy,
                col,
                36.0,
                t(self.lang, "next"),
                Action::NextPhrase,
                live,
                false,
            );
            yy += 42.0;
            self.lazer_button(
                x + pad,
                yy,
                col,
                32.0,
                t(self.lang, "loopA"),
                Action::LoopA,
                live,
                false,
            );
            self.lazer_button(
                x + pad + col + 5.0,
                yy,
                col,
                32.0,
                t(self.lang, "loopB"),
                Action::LoopB,
                live,
                false,
            );
            self.lazer_button(
                x + pad + (col + 5.0) * 2.0,
                yy,
                col,
                32.0,
                t(self.lang, "clearLoop"),
                Action::ClearLoop,
                live,
                false,
            );
            yy += 38.0;
            self.lazer_button(
                x + pad,
                yy,
                inner,
                32.0,
                t(self.lang, "loopPhrase"),
                Action::LoopPhrase,
                live,
                false,
            );
            yy += 40.0;
        }
        self.lazer_button(
            x + pad,
            yy,
            inner,
            34.0,
            t(self.lang, "restart"),
            Action::Restart,
            live,
            false,
        );

        // Song-level transport deliberately stays at the bottom of Control. Pause/resume
        // remains in the persistent dock so it never competes with these destructive actions.
        let transport_y = y + h - 48.0;
        self.lazer_button(
            x + pad,
            transport_y,
            col,
            40.0,
            t(self.lang, "prev"),
            Action::PrevPhrase,
            live && self.has_practice(),
            false,
        );
        self.outline_button(
            x + pad + col + 5.0,
            transport_y,
            col,
            40.0,
            t(self.lang, "skip"),
            Action::Skip,
            live,
            DANGER,
        );
        self.lazer_button(
            x + pad + (col + 5.0) * 2.0,
            transport_y,
            col,
            40.0,
            t(self.lang, "nextSong"),
            Action::NextSong,
            live,
            true,
        );
    }

    fn draw_queue(&mut self, x: f32, y: f32, h: f32) {
        let pad = 14.0;
        let list_y = y + 6.0;
        let list_h = h - 10.0;
        self.note_scroll_max(PAGE_QUEUE, self.queue.len() as f32 * QUEUE_ROW - list_h);
        clip(x, list_y, self.width, list_h);
        if self.queue.is_empty() {
            fill_text(
                x + self.width * 0.5,
                list_y + 72.0,
                14.0,
                MUTED,
                1,
                0,
                t(self.lang, "emptyQueue"),
            );
            fill_text(
                x + self.width * 0.5,
                list_y + 98.0,
                12.0,
                MUTED,
                1,
                0,
                t(self.lang, "reorder"),
            );
        } else {
            let scroll = self.display_scroll(PAGE_QUEUE);
            let controller = self.controller();
            let queue = self.queue.clone();
            let from = self.reorder_from;
            let to = if from.is_some() {
                Some(self.reorder_target())
            } else {
                None
            };
            for (index, entry) in queue.iter().enumerate() {
                if from == Some(index) {
                    continue;
                }
                let visual = visual_index(index, from, to);
                let ry = list_y + visual as f32 * QUEUE_ROW - scroll;
                if ry + QUEUE_ROW < list_y || ry > list_y + list_h {
                    continue;
                }
                self.draw_queue_row(
                    x + pad,
                    ry + 4.0,
                    self.width - pad * 2.0,
                    QUEUE_ROW - 8.0,
                    index,
                    entry,
                    controller,
                    false,
                );
            }
            if let Some(index) = from {
                if let Some(entry) = queue.get(index) {
                    let lift = self.reorder_finger_y - self.reorder_origin_y;
                    let ry = list_y + index as f32 * QUEUE_ROW - scroll + lift;
                    self.draw_queue_row(
                        x + pad,
                        ry + 2.0,
                        self.width - pad * 2.0,
                        QUEUE_ROW - 4.0,
                        index,
                        entry,
                        controller,
                        true,
                    );
                }
            }
        }
        unclip();
    }

    fn draw_queue_row(
        &mut self,
        x: f32,
        y: f32,
        w: f32,
        h: f32,
        index: usize,
        entry: &QueueEntry,
        controller: bool,
        lifted: bool,
    ) {
        fill_rect(x, y, w, h, if lifted { PANEL2 } else { ROW }, 8.0);
        if lifted {
            stroke_rect(x, y, w, h, PINK, 8.0, 1.5);
        }
        fill_rect(x, y, 4.0, h, PINK, 2.0);
        fill_text(
            x + 14.0,
            y + 16.0,
            11.0,
            PINK,
            0,
            1,
            &format!("{:02}", index + 1),
        );
        let text_w = if controller { w - 56.0 } else { w - 20.0 };
        fill_text(
            x + 40.0,
            y + 18.0,
            15.0,
            TEXT,
            0,
            1,
            &ellipsize(15.0, 1, &entry.title, text_w),
        );
        let meta = format!(
            "{}  ·  [{}]  ·  {}",
            empty(&entry.artist),
            entry.difficulty_name.as_deref().unwrap_or("—"),
            fmt_ms(entry.length_ms)
        );
        fill_text(
            x + 40.0,
            y + 40.0,
            11.0,
            MUTED,
            0,
            0,
            &ellipsize(11.0, 0, &meta, text_w),
        );
        let modes: Vec<&str> = entry.mods.iter().map(|item| readable_mod(item)).collect();
        let chips = format!(
            "Key {:+} · Speed {}%{}",
            entry.transpose,
            (entry.speed.max(0.5) * 100.0).round(),
            if modes.is_empty() {
                String::new()
            } else {
                format!(" · {}", modes.join(", "))
            }
        );
        fill_text(x + 40.0, y + 60.0, 11.0, GOLD, 0, 0, &chips);
        if controller {
            let hx = x + w - 40.0;
            fill_rect(hx, y + 18.0, 28.0, 42.0, PANEL, 6.0);
            for i in 0..3 {
                fill_rect(hx + 7.0, y + 28.0 + i as f32 * 8.0, 14.0, 2.0, MUTED, 1.0);
            }
            self.hit(hx - 4.0, y, 44.0, h, Action::QueueGrab(index));
            self.hit(x, y, w - 44.0, h, Action::QueueEdit(index));
        }
    }

    fn draw_info(&mut self, x: f32, y: f32, h: f32) {
        let pad = 14.0;
        let live = self.snapshot.has_gameplay();
        if self.info_view == InfoView::Overview {
            let scroll = self.display_scroll(PAGE_INFO);
            clip(x, y, self.width, h);
            let mut yy = y + 24.0 - scroll;
            fill_text(x + pad, yy, 18.0, TEXT, 0, 1, t(self.lang, "info"));
            yy += 28.0;
            fill_text(
                x + pad,
                yy,
                12.0,
                MUTED,
                0,
                0,
                if self.connected {
                    t(self.lang, "connected")
                } else {
                    t(self.lang, "closed")
                },
            );
            yy += 34.0;
            let song = self.snapshot.song_title.as_deref().unwrap_or("—");
            fill_rect(x + pad, yy, self.width - pad * 2.0, 74.0, ROW, 8.0);
            fill_text(
                x + pad + 12.0,
                yy + 22.0,
                14.0,
                TEXT,
                0,
                1,
                &ellipsize(14.0, 1, song, self.width - pad * 2.0 - 24.0),
            );
            fill_text(
                x + pad + 12.0,
                yy + 48.0,
                11.0,
                MUTED,
                0,
                0,
                &format!(
                    "{} · {}% · {}",
                    t(self.lang, "score"),
                    (self.snapshot.pitch_similarity * 100.0).round(),
                    t(self.lang, "similarity")
                ),
            );
            yy += 90.0;
            self.lazer_button(
                x + pad,
                yy,
                self.width - pad * 2.0,
                48.0,
                "Audio & output",
                Action::InfoAudio,
                true,
                false,
            );
            yy += 58.0;
            self.lazer_button(
                x + pad,
                yy,
                self.width - pad * 2.0,
                48.0,
                "Appearance",
                Action::InfoAppearance,
                true,
                false,
            );
            yy += 58.0;
            self.lazer_button(
                x + pad,
                yy,
                self.width - pad * 2.0,
                42.0,
                self.lang.label(),
                Action::Language,
                true,
                false,
            );
            yy += 54.0;
            fill_rect(x + pad, yy, self.width - pad * 2.0, 56.0, ROW, 8.0);
            fill_text(
                x + pad + 12.0,
                yy + 20.0,
                13.0,
                TEXT,
                0,
                1,
                t(self.lang, "remember"),
            );
            fill_text(
                x + pad + 12.0,
                yy + 39.0,
                10.0,
                MUTED,
                0,
                0,
                t(self.lang, "rememberHint"),
            );
            let on = self.remember;
            fill_rect(
                x + self.width - pad - 52.0,
                yy + 16.0,
                40.0,
                22.0,
                if on { PINK } else { LINE },
                11.0,
            );
            fill_rect(
                x + self.width - pad - (if on { 22.0 } else { 48.0 }),
                yy + 18.0,
                18.0,
                18.0,
                TEXT,
                9.0,
            );
            self.hit(
                x + pad,
                yy,
                self.width - pad * 2.0,
                56.0,
                Action::RememberDevice,
            );
            yy += 68.0;
            self.lazer_button(
                x + pad,
                yy,
                self.width - pad * 2.0,
                44.0,
                t(self.lang, "disconnect"),
                Action::Disconnect,
                true,
                false,
            );
            self.note_scroll_max(PAGE_INFO, yy + 64.0 - (y - scroll) - h);
            unclip();
            return;
        }
        if self.info_view == InfoView::Appearance {
            self.draw_appearance(x, y, h);
            return;
        }
        // Audio is intentionally a second-level page: sliders never appear on Info's landing page.
        self.draw_info_audio_header(x, y);
        let rows: [(&str, String); 8] = [
            (
                t(self.lang, "nowPlaying"),
                self.snapshot
                    .song_title
                    .clone()
                    .unwrap_or_else(|| "—".into()),
            ),
            (
                t(self.lang, "list"),
                format!(
                    "{} · {} · {}",
                    self.snapshot.song_artist.as_deref().unwrap_or("—"),
                    self.snapshot.song_difficulty.as_deref().unwrap_or("—"),
                    self.snapshot.song_creator.as_deref().unwrap_or("—")
                ),
            ),
            (t(self.lang, "score"), format!("{:.2}", self.snapshot.score)),
            (
                t(self.lang, "pitch"),
                self.snapshot
                    .detected_pitch_midi
                    .filter(|_| self.snapshot.voice_active)
                    .map(|midi| format!("{midi:.1} MIDI"))
                    .unwrap_or_else(|| "—".into()),
            ),
            (
                t(self.lang, "similarity"),
                format!("{}%", (self.snapshot.pitch_similarity * 100.0).round()),
            ),
            (
                t(self.lang, "phrase"),
                if self.snapshot.phrase_index >= 0 {
                    format!(
                        "{} / {}",
                        self.snapshot.phrase_index + 1,
                        self.snapshot.phrase_count
                    )
                } else {
                    "—".into()
                },
            ),
            (
                t(self.lang, "key"),
                format!("{:+}", self.snapshot.mixer.transpose),
            ),
            (
                t(self.lang, "std"),
                format!(
                    "{}% · {} – {}",
                    (self.snapshot.speed * 100.0).round(),
                    self.snapshot
                        .r#loop()
                        .a
                        .map(fmt_ms)
                        .unwrap_or_else(|| "—".into()),
                    self.snapshot
                        .r#loop()
                        .b
                        .map(fmt_ms)
                        .unwrap_or_else(|| "—".into())
                ),
            ),
        ];
        let scroll = self.display_scroll(PAGE_INFO);
        clip(x, y, self.width, h);
        let mut yy = y + 48.0 - scroll;
        for (label, value) in rows {
            fill_text(x + pad, yy, 12.0, MUTED, 0, 0, label);
            fill_text(
                x + self.width - pad,
                yy,
                13.0,
                TEXT,
                2,
                1,
                &ellipsize(13.0, 1, &value, self.width * 0.55),
            );
            yy += 28.0;
        }
        yy += 8.0;
        yy = self.slider(
            x,
            yy,
            "bgmVolume",
            t(self.lang, "bgm"),
            self.snapshot.mixer.background_music,
            0.0,
            1.0,
            true,
            live,
        );
        yy = self.slider(
            x,
            yy,
            "vocalsVolume",
            t(self.lang, "vocals"),
            self.snapshot.mixer.original_vocals,
            0.0,
            1.0,
            true,
            live,
        );
        yy = self.slider(
            x,
            yy,
            "monitorVolume",
            t(self.lang, "monitor"),
            self.snapshot.mixer.microphone_monitor,
            0.0,
            1.0,
            true,
            live,
        );
        yy = self.slider(
            x,
            yy,
            "microphoneLatency",
            t(self.lang, "micLat"),
            self.snapshot.mixer.microphone_latency,
            -500.0,
            1000.0,
            false,
            live,
        );
        yy = self.slider(
            x,
            yy,
            "accompanimentLatency",
            t(self.lang, "accLat"),
            self.snapshot.mixer.accompaniment_latency,
            -500.0,
            1000.0,
            false,
            live,
        );
        yy = self.slider(
            x,
            yy,
            "lyricsLatency",
            t(self.lang, "lyrLat"),
            self.snapshot.mixer.lyrics_latency,
            -500.0,
            1000.0,
            false,
            live,
        );

        let bw = (self.width - pad * 2.0 - 12.0) / 3.0;
        self.lazer_button(
            x + pad,
            yy,
            bw,
            40.0,
            t(self.lang, "loopA"),
            Action::LoopA,
            live && self.controller(),
            false,
        );
        self.lazer_button(
            x + pad + bw + 6.0,
            yy,
            bw,
            40.0,
            t(self.lang, "loopB"),
            Action::LoopB,
            live && self.controller(),
            false,
        );
        self.lazer_button(
            x + pad + (bw + 6.0) * 2.0,
            yy,
            bw,
            40.0,
            t(self.lang, "clearLoop"),
            Action::ClearLoop,
            live && self.controller(),
            false,
        );
        yy += 48.0;
        self.lazer_button(
            x + pad,
            yy,
            self.width - pad * 2.0,
            40.0,
            t(self.lang, "loopPhrase"),
            Action::LoopPhrase,
            live && self.controller(),
            false,
        );
        yy += 48.0;
        let tw = (self.width - pad * 2.0 - 8.0) * 0.5;
        self.lazer_button(
            x + pad,
            yy,
            tw,
            40.0,
            "OCT",
            Action::Octave,
            live && self.controller(),
            self.snapshot.mixer.octave_fold,
        );
        self.lazer_button(
            x + pad + tw + 8.0,
            yy,
            tw,
            40.0,
            "VOX",
            Action::Vocals,
            live && self.controller(),
            self.snapshot.mixer.original_vocals_enabled,
        );
        yy += 56.0;
        self.lazer_button(
            x + pad,
            yy,
            self.width - pad * 2.0,
            40.0,
            self.lang.label(),
            Action::Language,
            true,
            false,
        );
        yy += 52.0;
        fill_rect(x + pad, yy, self.width - pad * 2.0, 56.0, ROW, 8.0);
        fill_text(
            x + pad + 12.0,
            yy + 18.0,
            13.0,
            TEXT,
            0,
            1,
            t(self.lang, "remember"),
        );
        fill_text(
            x + pad + 12.0,
            yy + 38.0,
            10.0,
            MUTED,
            0,
            0,
            t(self.lang, "rememberHint"),
        );
        let on = self.remember;
        fill_rect(
            x + self.width - pad - 52.0,
            yy + 16.0,
            40.0,
            22.0,
            if on { PINK } else { LINE },
            11.0,
        );
        fill_rect(
            x + self.width - pad - (if on { 22.0 } else { 48.0 }),
            yy + 18.0,
            18.0,
            18.0,
            TEXT,
            9.0,
        );
        self.hit(
            x + pad,
            yy,
            self.width - pad * 2.0,
            56.0,
            Action::RememberDevice,
        );
        yy += 68.0;
        self.lazer_button(
            x + pad,
            yy,
            self.width - pad * 2.0,
            44.0,
            t(self.lang, "disconnect"),
            Action::Disconnect,
            true,
            false,
        );
        yy += 36.0;
        fill_text(
            x + self.width * 0.5,
            yy,
            12.0,
            MUTED,
            1,
            0,
            t(self.lang, "privacy"),
        );
        let content = yy + 28.0 - (y - scroll);
        self.note_scroll_max(PAGE_INFO, content - h);
        unclip();
    }

    fn draw_info_audio_header(&mut self, x: f32, y: f32) {
        self.lazer_button(
            x + 14.0,
            y + 8.0,
            82.0,
            30.0,
            "‹ Back",
            Action::InfoBack,
            true,
            false,
        );
        fill_text(x + 108.0, y + 23.0, 14.0, TEXT, 0, 1, "Audio & output");
    }

    fn draw_appearance(&mut self, x: f32, y: f32, h: f32) {
        let pad = 14.0;
        clip(x, y, self.width, h);
        self.lazer_button(
            x + pad,
            y + 12.0,
            82.0,
            32.0,
            "‹ Back",
            Action::InfoBack,
            true,
            false,
        );
        fill_text(x + pad, y + 70.0, 18.0, TEXT, 0, 1, "Appearance");
        fill_text(x + pad, y + 96.0, 12.0, MUTED, 0, 0, "Colour scheme");
        let w = (self.width - pad * 2.0 - 8.0) * 0.5;
        self.lazer_button(
            x + pad,
            y + 112.0,
            w,
            48.0,
            "Dark",
            Action::Theme(false),
            true,
            !self.light_theme,
        );
        self.lazer_button(
            x + pad + w + 8.0,
            y + 112.0,
            w,
            48.0,
            "Light",
            Action::Theme(true),
            true,
            self.light_theme,
        );
        fill_text(
            x + pad,
            y + 192.0,
            12.0,
            MUTED,
            0,
            0,
            "Modern osu!-inspired surfaces",
        );
        fill_text(
            x + pad,
            y + 214.0,
            11.0,
            MUTED,
            0,
            0,
            "Your preference is remembered on this device.",
        );
        self.note_scroll_max(PAGE_INFO, 0.0);
        unclip();
    }

    fn draw_sheet(
        &mut self,
        kind: SheetKind,
        id: &str,
        title: &str,
        artist: &str,
        speed: f64,
        transpose: i32,
        mods: &[(String, String, bool)],
    ) {
        fill_rect(0.0, 0.0, self.width, self.height, DIM, 0.0);
        self.hit(0.0, 0.0, self.width, self.height, Action::Back);
        // Song setup is a dedicated full-screen task rather than a partial sheet: long
        // two-column MOD lists remain readable and cannot collide with the bottom dock.
        let y = self.inset_t;
        let h = self.height - self.inset_t - self.inset_b;
        fill_rect(0.0, y, self.width, h, PANEL, 12.0);
        fill_rect(0.0, y, self.width, 3.0, PINK, 0.0);
        fill_text(
            20.0,
            y + 28.0,
            18.0,
            TEXT,
            0,
            1,
            &ellipsize(18.0, 1, title, self.width - 110.0),
        );
        fill_text(20.0, y + 50.0, 13.0, MUTED, 0, 0, artist);
        fill_text(
            self.width - 20.0,
            y + 28.0,
            13.0,
            MUTED,
            2,
            0,
            t(self.lang, "cancel"),
        );
        self.hit(self.width - 90.0, y, 90.0, 48.0, Action::CloseSheet);

        let pad = 14.0;
        let key = format!("{transpose:+}");
        self.stepper(
            pad,
            y + 70.0,
            self.width - pad * 2.0,
            t(self.lang, "key"),
            &key,
            Action::SheetKey(-1),
            Action::SheetKey(0),
            Action::SheetKey(1),
            true,
        );
        let speed_s = format!("{}%", (speed * 100.0).round());
        self.stepper(
            pad,
            y + 126.0,
            self.width - pad * 2.0,
            t(self.lang, "std"),
            &speed_s,
            Action::SheetSpeed(-0.05),
            Action::SheetSpeed(0.0),
            Action::SheetSpeed(0.05),
            true,
        );

        fill_text(pad, y + 192.0, 12.0, MUTED, 0, 0, t(self.lang, "mods"));
        // Fixed two-column, 44px controls are deliberately less dense than the old
        // word-width chips. They keep every MOD target thumb-sized and prevent a
        // layout shift (for example after NC/DC arrives) from changing what a tap hits.
        let gap = 8.0;
        let mod_w = (self.width - pad * 2.0 - gap) * 0.5;
        let mut my = y + 206.0;
        for (index, (acronym, name, on)) in mods.iter().enumerate() {
            let column = (index % 2) as f32;
            if index > 0 && index % 2 == 0 {
                my += 52.0;
            }
            let mx = pad + column * (mod_w + gap);
            fill_rect(
                mx,
                my,
                mod_w,
                44.0,
                if *on { 0x33E846A0 } else { PANEL2 },
                8.0,
            );
            stroke_rect(mx, my, mod_w, 44.0, if *on { PINK } else { LINE }, 8.0, 1.0);
            fill_text(
                mx + 10.0,
                my + 16.0,
                13.0,
                if *on { PINK } else { TEXT },
                0,
                1,
                acronym,
            );
            fill_text(
                mx + 10.0,
                my + 32.0,
                10.0,
                MUTED,
                0,
                1,
                &ellipsize(10.0, 1, name, mod_w - 20.0),
            );
            self.hit(mx, my, mod_w, 44.0, Action::ToggleMod(acronym.clone()));
        }

        let by = y + h - 64.0 - self.inset_b;
        match kind {
            SheetKind::Add => {
                if self.role == "spectator" {
                    self.lazer_button(
                        pad,
                        by,
                        self.width - pad * 2.0,
                        48.0,
                        t(self.lang, "add"),
                        Action::QueueAdd,
                        true,
                        true,
                    );
                } else {
                    let w = (self.width - pad * 2.0 - 12.0) / 3.0;
                    self.lazer_button(
                        pad,
                        by,
                        w,
                        48.0,
                        t(self.lang, "add"),
                        Action::QueueAdd,
                        true,
                        false,
                    );
                    self.lazer_button(
                        pad + w + 6.0,
                        by,
                        w,
                        48.0,
                        t(self.lang, "addNext"),
                        Action::QueueAddNext,
                        true,
                        false,
                    );
                    self.lazer_button(
                        pad + (w + 6.0) * 2.0,
                        by,
                        w,
                        48.0,
                        t(self.lang, "playNow"),
                        Action::PlayNow,
                        self.snapshot.has_gameplay(),
                        true,
                    );
                }
            }
            SheetKind::Edit => {
                let w = (self.width - pad * 2.0 - 12.0) / 3.0;
                self.outline_button(
                    pad,
                    by,
                    w,
                    48.0,
                    t(self.lang, "remove"),
                    Action::QueueRemove(id.into()),
                    self.controller(),
                    DANGER,
                );
                self.lazer_button(
                    pad + w + 6.0,
                    by,
                    w,
                    48.0,
                    t(self.lang, "playNow"),
                    Action::QueuePlay(id.into()),
                    self.controller(),
                    true,
                );
                self.lazer_button(
                    pad + (w + 6.0) * 2.0,
                    by,
                    w,
                    48.0,
                    t(self.lang, "save"),
                    Action::QueueAdd,
                    self.controller(),
                    false,
                );
            }
        }
    }

    fn draw_confirm(&mut self, confirm: Confirm) {
        fill_rect(0.0, 0.0, self.width, self.height, DIM, 0.0);
        self.hit(0.0, 0.0, self.width, self.height, Action::Back);
        let box_w = self.width - 48.0;
        let box_h = 168.0;
        let bx = 24.0;
        let by = (self.height - box_h) * 0.5;
        fill_rect(bx, by, box_w, box_h, PANEL, 10.0);
        stroke_rect(bx, by, box_w, box_h, LINE, 10.0, 1.0);
        fill_rect(bx, by, box_w, 3.0, PINK, 0.0);
        let msg = match confirm {
            Confirm::Skip => t(self.lang, "confirmSkip"),
            Confirm::Clear => t(self.lang, "confirmClear"),
        };
        fill_text(self.width * 0.5, by + 56.0, 15.0, TEXT, 1, 1, msg);
        self.lazer_button(
            bx + 16.0,
            by + 100.0,
            (box_w - 44.0) * 0.5,
            44.0,
            t(self.lang, "cancel"),
            Action::ConfirmNo,
            true,
            false,
        );
        self.lazer_button(
            bx + 28.0 + (box_w - 44.0) * 0.5,
            by + 100.0,
            (box_w - 44.0) * 0.5,
            44.0,
            t(self.lang, "yes"),
            Action::ConfirmYes,
            true,
            true,
        );
    }

    fn compact_stepper(
        &mut self,
        x: f32,
        y: f32,
        w: f32,
        label: &str,
        value: &str,
        left: Action,
        reset: Action,
        right: Action,
        enabled: bool,
    ) {
        fill_rect(x, y, w, 38.0, ROW, 6.0);
        fill_text(x + 8.0, y + 19.0, 11.0, MUTED, 0, 0, label);
        let bw = 28.0;
        let right_x = x + w - 8.0 - bw;
        let mid_x = right_x - 4.0 - 44.0;
        let left_x = mid_x - 4.0 - bw;
        self.lazer_button(left_x, y + 5.0, bw, 28.0, "−", left, enabled, false);
        self.lazer_button(mid_x, y + 5.0, 44.0, 28.0, value, reset, enabled, false);
        self.lazer_button(right_x, y + 5.0, bw, 28.0, "+", right, enabled, false);
    }

    fn stepper(
        &mut self,
        x: f32,
        y: f32,
        w: f32,
        label: &str,
        value: &str,
        left: Action,
        reset: Action,
        right: Action,
        enabled: bool,
    ) {
        fill_rect(x, y, w, 46.0, ROW, 8.0);
        fill_text(x + 12.0, y + 23.0, 12.0, MUTED, 0, 0, label);
        let bw = 40.0;
        let right_x = x + w - 12.0 - bw;
        let mid_x = right_x - 8.0 - 64.0;
        let left_x = mid_x - 8.0 - bw;
        self.lazer_button(left_x, y + 7.0, bw, 32.0, "−", left, enabled, false);
        self.lazer_button(mid_x, y + 7.0, 64.0, 32.0, value, reset, enabled, false);
        self.lazer_button(right_x, y + 7.0, bw, 32.0, "+", right, enabled, false);
    }

    fn outline_button(
        &mut self,
        x: f32,
        y: f32,
        w: f32,
        h: f32,
        label: &str,
        action: Action,
        enabled: bool,
        accent: u32,
    ) {
        fill_rect(x, y, w, h, PANEL2, 6.0);
        stroke_rect(x, y, w, h, if enabled { accent } else { LINE }, 6.0, 1.0);
        fill_text(
            x + w * 0.5,
            y + h * 0.5,
            13.0,
            if enabled { accent } else { MUTED },
            1,
            1,
            label,
        );
        if enabled && self.controller() {
            self.hit(x, y, w, h, action);
        }
    }

    fn lazer_button(
        &mut self,
        x: f32,
        y: f32,
        w: f32,
        h: f32,
        label: &str,
        action: Action,
        enabled: bool,
        filled: bool,
    ) {
        if filled && enabled {
            fill_rect(x, y, w, h, PINK, 6.0);
            fill_text(x + w * 0.5, y + h * 0.5, 13.0, TEXT, 1, 1, label);
        } else {
            fill_rect(x, y, w, h, PANEL2, 6.0);
            stroke_rect(
                x,
                y,
                w,
                h,
                if enabled { LINE } else { 0xFF2A2A32 },
                6.0,
                1.0,
            );
            fill_text(
                x + w * 0.5,
                y + h * 0.5,
                13.0,
                if enabled { TEXT } else { MUTED },
                1,
                1,
                label,
            );
        }
        if enabled
            && (self.controller()
                || matches!(
                    action,
                    Action::Language
                        | Action::Disconnect
                        | Action::ConfirmNo
                        | Action::ConfirmYes
                        | Action::CloseSheet
                        | Action::Back
                        | Action::QueueAdd
                        | Action::Page(_)
                ))
        {
            self.hit(x, y, w, h, action);
        }
    }

    fn slider(
        &mut self,
        x: f32,
        y: f32,
        name: &'static str,
        label: &str,
        value: f64,
        min: f64,
        max: f64,
        percent: bool,
        enabled: bool,
    ) -> f32 {
        let pad = 14.0;
        let readout = if percent {
            format!("{}%", (value * 100.0).round())
        } else {
            format!("{:.0} ms", value)
        };
        fill_text(x + pad, y, 12.0, MUTED, 0, 0, label);
        fill_text(x + self.width - pad, y, 12.0, TEXT, 2, 0, &readout);
        let sl = x + pad;
        let sw = self.width - pad * 2.0;
        fill_rect(sl, y + 18.0, sw, 6.0, LINE, 3.0);
        let t = ((value - min) / (max - min)).clamp(0.0, 1.0) as f32;
        fill_rect(sl, y + 18.0, sw * t, 6.0, PINK, 3.0);
        fill_rect(sl + sw * t - 6.0, y + 14.0, 12.0, 14.0, TEXT, 6.0);
        if enabled && self.controller() {
            let ratio = ((self.px - sl) / sw).clamp(0.0, 1.0) as f64;
            let next = min + (max - min) * ratio;
            let action = if name.ends_with("Latency") {
                Action::Latency(name, next)
            } else {
                Action::Mixer(name, next)
            };
            self.hit(sl, y + 8.0, sw, 28.0, action);
        }
        y + 48.0
    }

    fn hit(&mut self, x: f32, y: f32, w: f32, h: f32, action: Action) {
        self.hits.push(Hit { x, y, w, h, action });
    }

    fn note_scroll_max(&mut self, page: i32, max: f32) {
        self.scroll_max[page as usize] = max.max(0.0);
    }
}

fn visual_index(index: usize, from: Option<usize>, to: Option<usize>) -> usize {
    match (from, to) {
        (Some(from), Some(to)) if from < to && index > from && index <= to => index - 1,
        (Some(from), Some(to)) if from > to && index >= to && index < from => index + 1,
        _ => index,
    }
}

fn empty(value: &str) -> &str {
    if value.is_empty() {
        "—"
    } else {
        value
    }
}

fn fmt_ms(ms: f64) -> String {
    let total = (ms.max(0.0) / 1000.0) as i32;
    format!("{}:{:02}", total / 60, total % 60)
}

fn ellipsize(size: f32, flags: i32, text: &str, max_w: f32) -> String {
    if max_w <= 8.0 || measure(size, flags, text) <= max_w {
        return text.to_string();
    }
    let mut s = text.to_string();
    while !s.is_empty() {
        let next = format!("{s}…");
        if measure(size, flags, &next) <= max_w {
            return next;
        }
        s.pop();
    }
    "…".into()
}

fn readable_mod(acronym: &str) -> &str {
    match acronym {
        "IQ" => "Immersive",
        "NF" => "No Fail",
        "RX" => "Relax",
        "VOX" => "Vocals",
        "OCT" => "Octave",
        "NC" => "Nightcore",
        "DC" => "Daycore",
        "AT" => "Auto",
        "REC" => "Record",
        "PR" => "Practice",
        _ => acronym,
    }
}

fn diff_color(name: &str) -> u32 {
    let n = name.to_ascii_lowercase();
    if n.contains("easy") || n.contains("basic") {
        OK
    } else if n.contains("normal") || n.contains("std") {
        BLUE
    } else if n.contains("hard") || n.contains("hyper") {
        GOLD
    } else if n.contains("insane") || n.contains("another") {
        PINK
    } else if n.contains("expert") || n.contains("extra") || n.contains("oni") {
        PURPLE
    } else {
        PINK
    }
}

fn silent_error(error: &str) -> bool {
    matches!(
        error,
        "transition_busy"
            | "Command rate limit exceeded."
            | "Sequence was replayed or out of order."
            | ""
    )
}

fn friendly_error(lang: Lang, error: &str) -> &str {
    match error {
        "no_active_gameplay" => t(lang, "idle"),
        "The queue is empty." => t(lang, "queueEmpty"),
        "transition_busy" => t(lang, "switching"),
        _ if error.is_empty() => t(lang, "failed"),
        _ => error,
    }
}

fn default_mods() -> Vec<(String, String, bool)> {
    default_mods_from(&[])
}

fn default_mods_from(enabled: &[String]) -> Vec<(String, String, bool)> {
    [
        ("IQ", "Immersive"),
        ("NF", "No Fail"),
        ("RX", "Relax"),
        ("VOX", "Vocals"),
        ("OCT", "Octave"),
        ("NC", "Nightcore"),
        ("DC", "Daycore"),
        ("AT", "Auto"),
        ("REC", "Record"),
        ("PR", "Practice"),
    ]
    .into_iter()
    .map(|(a, n)| (a.into(), n.into(), enabled.iter().any(|item| item == a)))
    .collect()
}
