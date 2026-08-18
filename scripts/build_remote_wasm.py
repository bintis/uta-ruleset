#!/usr/bin/env python3
"""Build the Rust canvas remote and inline it into a single HTML file."""

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


def build_with_cargo() -> bytes | None:
    cargo = shutil.which("cargo")
    if cargo is None:
        return None
    try:
        env = dict(**__import__("os").environ)
        env["RUSTFLAGS"] = (env.get("RUSTFLAGS", "") + " -C link-arg=--initial-memory=16777216 -C link-arg=--max-memory=67108864").strip()
        subprocess.run(
            [cargo, "build", "--manifest-path", str(CRATE / "Cargo.toml"), "--release", "--target", "wasm32-unknown-unknown"],
            cwd=ROOT,
            check=True,
            env=env,
        )
    except subprocess.CalledProcessError:
        return None

    output = CRATE / "target/wasm32-unknown-unknown/release/uta_remote_wasm.wasm"
    return output.read_bytes() if output.exists() else None


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--require-rust", action="store_true", help="fail if the wasm32 crate cannot be built")
    args = parser.parse_args()

    wasm = build_with_cargo()
    if wasm is None:
        if args.require_rust:
            raise SystemExit("Rust wasm32 toolchain is unavailable or the crate failed to build")
        raise SystemExit("uta! remote now requires a Rust wasm32 build; the tiny fallback ABI is gone")

    if not wasm.startswith(b"\0asm"):
        raise SystemExit("generated payload is not a WebAssembly module")

    template = TEMPLATE.read_text(encoding="utf-8")
    if template.count("__UTA_WASM_BASE64__") != 1:
        raise SystemExit("remote template must contain exactly one WASM placeholder")
    html = template.replace("__UTA_WASM_BASE64__", base64.b64encode(wasm).decode("ascii"))
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(html, encoding="utf-8")
    print(f"wrote {OUTPUT.relative_to(ROOT)}: {len(wasm)} WASM bytes, {len(html.encode('utf-8'))} HTML bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
