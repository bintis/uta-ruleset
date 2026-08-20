mod app;
mod bin;
mod i18n;
mod proto;

use app::App;
use i18n::Lang;
use std::sync::atomic::{AtomicBool, AtomicI32, Ordering};
use std::sync::Mutex;

static LIGHT_THEME: AtomicBool = AtomicBool::new(false);
// 0 Modern, 1 osu!-inspired, 2 Aurora (all Canvas/Rust rendered).
static RENDER_STYLE: AtomicI32 = AtomicI32::new(0);

#[link(wasm_import_module = "env")]
extern "C" {
    fn host_fill_rect(x: f32, y: f32, w: f32, h: f32, color: u32, radius: f32);
    fn host_stroke_rect(x: f32, y: f32, w: f32, h: f32, color: u32, radius: f32, width: f32);
    fn host_fill_triangle(x1: f32, y1: f32, x2: f32, y2: f32, x3: f32, y3: f32, color: u32);
    fn host_fill_text(
        x: f32,
        y: f32,
        size: f32,
        color: u32,
        align: i32,
        flags: i32,
        ptr: *const u8,
        len: usize,
    );
    fn host_measure_text(size: f32, flags: i32, ptr: *const u8, len: usize) -> f32;
    fn host_clip(x: f32, y: f32, w: f32, h: f32);
    fn host_unclip();
    fn host_send(ptr: *const u8, len: usize);
    fn host_search(x: f32, y: f32, w: f32, h: f32, visible: i32);
    fn host_log(ptr: *const u8, len: usize);
    fn host_session(id_ptr: *const u8, id_len: usize, secret_ptr: *const u8, secret_len: usize);
    fn host_seq(value: i32);
    fn host_remember(enabled: i32);
    fn host_theme(light: i32);
    fn host_style(style: i32);
}

static APP: Mutex<Option<App>> = Mutex::new(None);

#[no_mangle]
pub extern "C" fn alloc(size: usize) -> *mut u8 {
    let mut buf = Vec::<u8>::with_capacity(size);
    let ptr = buf.as_mut_ptr();
    std::mem::forget(buf);
    ptr
}

#[no_mangle]
pub extern "C" fn dealloc(ptr: *mut u8, size: usize) {
    if ptr.is_null() || size == 0 {
        return;
    }
    unsafe {
        drop(Vec::from_raw_parts(ptr, 0, size));
    }
}

#[no_mangle]
pub extern "C" fn start(
    lang_ptr: *const u8,
    lang_len: usize,
    reduced: i32,
    sequence: i32,
    remember: i32,
) {
    let lang = Lang::parse(&read(lang_ptr, lang_len));
    log("ui.start binary+16MiB heap");
    if let Ok(mut slot) = APP.lock() {
        *slot = Some(App::new(lang, reduced != 0, sequence, remember != 0));
    }
}

#[no_mangle]
pub extern "C" fn resize(w: f32, h: f32, inset_t: f32, inset_b: f32) {
    with(|app| app.resize(w, h, inset_t, inset_b));
}

#[no_mangle]
pub extern "C" fn pointer(kind: i32, x: f32, y: f32) {
    with(|app| app.pointer(kind, x, y));
}

#[no_mangle]
pub extern "C" fn wheel(x: f32, y: f32, dy: f32) {
    with(|app| app.wheel(x, y, dy));
}

#[no_mangle]
pub extern "C" fn set_search(ptr: *const u8, len: usize) {
    let text = read(ptr, len);
    with(|app| app.set_search(text));
}

#[no_mangle]
pub extern "C" fn on_message(ptr: *const u8, len: usize) {
    if ptr.is_null() || len == 0 {
        return;
    }
    let bytes = unsafe { std::slice::from_raw_parts(ptr, len) };
    with(|app| app.on_bytes(bytes));
}

#[no_mangle]
pub extern "C" fn set_closed() {
    with(|app| app.set_closed());
}

#[no_mangle]
pub extern "C" fn set_theme(light: i32) {
    let light = light != 0;
    LIGHT_THEME.store(light, Ordering::Relaxed);
    with(|app| app.set_theme(light));
}

#[no_mangle]
pub extern "C" fn set_style(style: i32) {
    let style = style.clamp(0, 2);
    RENDER_STYLE.store(style, Ordering::Relaxed);
    with(|app| app.set_style(style));
}

#[no_mangle]
pub extern "C" fn demo() {
    with(|app| app.load_demo());
}

#[no_mangle]
pub extern "C" fn frame(now: f64) {
    with(|app| app.frame(now));
}

fn with(func: impl FnOnce(&mut App)) {
    if let Ok(mut slot) = APP.lock() {
        if let Some(app) = slot.as_mut() {
            func(app);
        }
    }
}

fn read(ptr: *const u8, len: usize) -> String {
    if ptr.is_null() || len == 0 {
        return String::new();
    }
    unsafe { String::from_utf8_lossy(std::slice::from_raw_parts(ptr, len)).into_owned() }
}

pub(crate) fn fill_rect(x: f32, y: f32, w: f32, h: f32, color: u32, radius: f32) {
    unsafe { host_fill_rect(x, y, w, h, paint(color), radius) }
}

pub(crate) fn stroke_rect(x: f32, y: f32, w: f32, h: f32, color: u32, radius: f32, width: f32) {
    unsafe { host_stroke_rect(x, y, w, h, paint(color), radius, width) }
}

pub(crate) fn fill_triangle(x1: f32, y1: f32, x2: f32, y2: f32, x3: f32, y3: f32, color: u32) {
    unsafe { host_fill_triangle(x1, y1, x2, y2, x3, y3, color) }
}

pub(crate) fn fill_text(x: f32, y: f32, size: f32, color: u32, align: i32, flags: i32, text: &str) {
    unsafe {
        host_fill_text(
            x,
            y,
            size,
            paint(color),
            align,
            flags,
            text.as_ptr(),
            text.len(),
        )
    }
}

fn paint(color: u32) -> u32 {
    let style = RENDER_STYLE.load(Ordering::Relaxed);
    let osu = style == 1;
    let aurora = style == 2;
    let light = LIGHT_THEME.load(Ordering::Relaxed);
    // The two retained prototype skins share semantics and layout but intentionally use
    // distinct surface/accent families. This mapping applies to every canvas primitive.
    match (osu, light, color) {
        // Aurora: a separate cyan/indigo Canvas skin, not an HTML theme.
        (false, false, 0xFF16161C) if aurora => 0xFF09121D,
        (false, false, 0xF216161C) if aurora => 0xF20A1420,
        (false, false, 0xFF2A2433 | 0xFF221E28) if aurora => 0xFF10283A,
        (false, false, 0xFF33303C) if aurora => 0xFF15364D,
        (false, false, 0xFF4A4456) if aurora => 0xFF2B6683,
        (false, false, 0xFFE846A0) if aurora => 0xFF35D6E8,
        // Modern dark: neutral system-controller surfaces.
        (false, false, 0xFF16161C) => 0xFF0B0B0E,
        (false, false, 0xF216161C) => 0xF20B0B0E,
        (false, false, 0xFF2A2433 | 0xFF221E28) => 0xFF17171C,
        (false, false, 0xFF33303C) => 0xFF202027,
        (false, false, 0xFF4A4456) => 0xFF474750,
        (false, false, 0xFFE846A0) => 0xFFFF4AAA,
        // osu!-inspired dark: purple stage surfaces and brighter pink/violet energy.
        (true, false, 0xFF16161C) => 0xFF17131E,
        (true, false, 0xF216161C) => 0xF2120E17,
        (true, false, 0xFF2A2433 | 0xFF221E28) => 0xFF251F2D,
        (true, false, 0xFF33303C) => 0xFF332A3D,
        (true, false, 0xFF4A4456) => 0xFF5B4B62,
        (true, false, 0xFFE846A0) => 0xFFFF66AA,
        // Modern light.
        (false, true, 0xFF16161C) => 0xFFF5F4F7,
        (false, true, 0xF216161C) => 0xF2F8F7FA,
        (false, true, 0xFF2A2433 | 0xFF221E28) => 0xFFFFFFFF,
        (false, true, 0xFF33303C) => 0xFFF2EFF5,
        (false, true, 0xFF4A4456) => 0xFFD8D2DC,
        (false, true, 0xFFE846A0) => 0xFFE83294,
        // osu!-inspired light.
        (true, true, 0xFF16161C) => 0xFFFFF7FB,
        (true, true, 0xF216161C) => 0xF2FFF9FC,
        (true, true, 0xFF2A2433 | 0xFF221E28) => 0xFFFFFFFF,
        (true, true, 0xFF33303C) => 0xFFF8EDF4,
        (true, true, 0xFF4A4456) => 0xFFE3D2E0,
        (true, true, 0xFFE846A0) => 0xFFEF4F9C,
        // Shared light contrast mapping.
        (_, true, 0xFFFFFFFF) => 0xFF292431,
        (_, true, 0xFFB0A8B8) => 0xFF746C7B,
        (_, true, 0xCC0A0A0E) => 0x88908A98,
        (_, true, 0xF21A1A22) => 0xF2FFFFFF,
        (_, true, 0xFF2A2A32) => 0xFFE5E0EA,
        (_, true, 0x6A000000) => 0x12000000,
        _ => color,
    }
}

pub(crate) fn measure(size: f32, flags: i32, text: &str) -> f32 {
    unsafe { host_measure_text(size, flags, text.as_ptr(), text.len()) }
}

pub(crate) fn clip(x: f32, y: f32, w: f32, h: f32) {
    unsafe { host_clip(x, y, w, h) }
}

pub(crate) fn unclip() {
    unsafe { host_unclip() }
}

pub(crate) fn send(bytes: &[u8]) {
    unsafe { host_send(bytes.as_ptr(), bytes.len()) }
}

pub(crate) fn log(text: &str) {
    unsafe { host_log(text.as_ptr(), text.len()) }
}

pub(crate) fn store_session(id: &str, secret: &str) {
    unsafe { host_session(id.as_ptr(), id.len(), secret.as_ptr(), secret.len()) }
}

pub(crate) fn store_seq(value: i32) {
    unsafe { host_seq(value) }
}

pub(crate) fn set_remembered(enabled: bool) {
    unsafe { host_remember(i32::from(enabled)) }
}

pub(crate) fn persist_theme(light: bool) {
    LIGHT_THEME.store(light, Ordering::Relaxed);
    unsafe { host_theme(i32::from(light)) }
}

pub(crate) fn persist_style(style: i32) {
    let style = style.clamp(0, 2);
    RENDER_STYLE.store(style, Ordering::Relaxed);
    unsafe { host_style(style) }
}

pub(crate) fn search_rect(x: f32, y: f32, w: f32, h: f32, visible: bool) {
    unsafe { host_search(x, y, w, h, i32::from(visible)) }
}
