#![no_std]

/// Monotonic command-sequence helper used by the embedded mobile client.
/// The checked-in fallback WASM exports the same ABI when the Rust target is
/// unavailable in a packaging environment.
#[no_mangle]
pub extern "C" fn next_sequence(value: i32) -> i32 {
    value.saturating_add(1)
}

#[panic_handler]
fn panic(_: &core::panic::PanicInfo<'_>) -> ! {
    loop {}
}
