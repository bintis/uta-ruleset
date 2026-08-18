#!/usr/bin/env python3
"""Build the tiny Rust protocol helper and inline it into the mobile HTML."""

from __future__ import annotations

import argparse
import base64
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CRATE = ROOT / "web/uta-remote-wasm"
TEMPLATE = CRATE / "uta-remote.template.html"
OUTPUT = ROOT / "osu.Game.Rulesets.Uta/Remote/Assets/uta-remote.html"
FALLBACK = CRATE / "fallback-core.wasm"


def build_with_cargo() -> bytes | None:
    cargo = shutil.which("cargo")
    if cargo is None:
        return None
    try:
        subprocess.run(
            [cargo, "build", "--manifest-path", str(CRATE / "Cargo.toml"), "--release", "--target", "wasm32-unknown-unknown"],
            cwd=ROOT,
            check=True,
        )
    except subprocess.CalledProcessError:
        return None

    output = CRATE / "target/wasm32-unknown-unknown/release/uta_remote_wasm.wasm"
    return output.read_bytes() if output.exists() else None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--require-rust", action="store_true", help="fail instead of using the audited fallback core")
    args = parser.parse_args()

    wasm = build_with_cargo()
    source = "Rust release build"
    if wasm is None:
        if args.require_rust:
            raise SystemExit("Rust wasm32 toolchain is unavailable or the crate failed to build")
        wasm = FALLBACK.read_bytes()
        source = "checked-in ABI-equivalent fallback"

    if not wasm.startswith(b"\0asm"):
        raise SystemExit("generated payload is not a WebAssembly module")

    template = TEMPLATE.read_text(encoding="utf-8")
    if template.count("__UTA_WASM_BASE64__") != 1:
        raise SystemExit("remote template must contain exactly one WASM placeholder")
    html = template.replace("__UTA_WASM_BASE64__", base64.b64encode(wasm).decode("ascii"))
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(html, encoding="utf-8")
    print(f"wrote {OUTPUT.relative_to(ROOT)} using {source}: {len(wasm)} WASM bytes, {len(html.encode('utf-8'))} HTML bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
