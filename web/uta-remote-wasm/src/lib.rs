mod app;
mod bin;
mod i18n;
mod proto;

use app::App;
use i18n::Lang;
use std::sync::Mutex;

#[link(wasm_import_module = "env")]
extern "C" {
    fn host_fill_rect(x: f32, y: f32, w: f32, h: f32, color: u32, radius: f32);
    fn host_stroke_rect(x: f32, y: f32, w: f32, h: f32, color: u32, radius: f32, width: f32);
    fn host_fill_triangle(x1: f32, y1: f32, x2: f32, y2: f32, x3: f32, y3: f32, color: u32);
    fn host_fill_text(x: f32, y: f32, size: f32, color: u32, align: i32, flags: i32, ptr: *const u8, len: usize);
    fn host_measure_text(size: f32, flags: i32, ptr: *const u8, len: usize) -> f32;
    fn host_clip(x: f32, y: f32, w: f32, h: f32);
    fn host_unclip();
    fn host_send(ptr: *const u8, len: usize);
    fn host_search(x: f32, y: f32, w: f32, h: f32, visible: i32);
    fn host_log(ptr: *const u8, len: usize);
    fn host_session(id_ptr: *const u8, id_len: usize, secret_ptr: *const u8, secret_len: usize);
    fn host_seq(value: i32);
    fn host_remember(enabled: i32);
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
pub extern "C" fn start(lang_ptr: *const u8, lang_len: usize, reduced: i32, sequence: i32, remember: i32) {
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
    unsafe { host_fill_rect(x, y, w, h, color, radius) }
}

pub(crate) fn stroke_rect(x: f32, y: f32, w: f32, h: f32, color: u32, radius: f32, width: f32) {
    unsafe { host_stroke_rect(x, y, w, h, color, radius, width) }
}

pub(crate) fn fill_triangle(x1: f32, y1: f32, x2: f32, y2: f32, x3: f32, y3: f32, color: u32) {
    unsafe { host_fill_triangle(x1, y1, x2, y2, x3, y3, color) }
}

pub(crate) fn fill_text(x: f32, y: f32, size: f32, color: u32, align: i32, flags: i32, text: &str) {
    unsafe { host_fill_text(x, y, size, color, align, flags, text.as_ptr(), text.len()) }
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

pub(crate) fn search_rect(x: f32, y: f32, w: f32, h: f32, visible: bool) {
    unsafe { host_search(x, y, w, h, i32::from(visible)) }
}
