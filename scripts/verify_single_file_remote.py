#!/usr/bin/env python3
"""Verify that the mobile controller is one self-contained, executable HTML file."""

from __future__ import annotations

import base64
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HTML = ROOT / "osu.Game.Rulesets.Uta/Remote/Assets/uta-remote.html"

REQUIRED_EXPORTS = ("alloc", "dealloc", "start", "resize", "pointer", "frame", "on_message", "demo")


def main() -> int:
    text = HTML.read_text(encoding="utf-8")
    failures: list[str] = []
    if "__UTA_WASM_BASE64__" in text:
        failures.append("unexpanded WASM placeholder")
    if re.search(r"<(?:script|img|link)\b[^>]+(?:src|href)\s*=\s*['\"](?:https?:)?//", text, re.I):
        failures.append("external script/image/style URL")
    if re.search(r"@import\s+url\s*\(", text, re.I):
        failures.append("external CSS import")
    if "WebSocket" not in text or "sessionStorage" not in text:
        failures.append("remote protocol/reconnect client is missing")
    if 'id="c"' not in text and "<canvas" not in text:
        failures.append("canvas host is missing")
    if 'id="search"' not in text:
        failures.append("native search input is missing")
    if "prefers-reduced-motion" not in text:
        failures.append("reduced-motion CSS is missing")
    if not all(token in text for token in ("English", "中文", "日本語")):
        failures.append("English/Chinese/Japanese language selector is incomplete")

    match = re.search(r"const WASM_BASE64='([A-Za-z0-9+/=]+)'", text)
    if match is None:
        failures.append("embedded WASM payload not found")
        wasm = b""
    else:
        wasm = base64.b64decode(match.group(1), validate=True)
        if not wasm.startswith(b"\0asm"):
            failures.append("embedded payload does not have the WASM magic")

    node = shutil.which("node")
    if wasm and node:
        with tempfile.NamedTemporaryFile(suffix=".wasm") as temporary:
            temporary.write(wasm)
            temporary.flush()
            stubs = ",".join(f"{name}:()=>0" for name in (
                "host_fill_rect", "host_stroke_rect", "host_fill_triangle", "host_fill_text", "host_measure_text",
                "host_clip", "host_unclip", "host_send", "host_search",
                "host_log", "host_session", "host_seq", "host_remember", "host_theme", "host_style",
            ))
            needed = ",".join(f"'{name}'" for name in REQUIRED_EXPORTS)
            script = (
                "const fs=require('fs');"
                f"const env={{{stubs}}};"
                "WebAssembly.instantiate(fs.readFileSync(process.argv[1]),{env}).then(x=>{"
                f"const need=[{needed}];"
                "for(const name of need)if(typeof x.instance.exports[name]!=='function')process.exit(3);"
                "}).catch(()=>process.exit(2));"
            )
            completed = subprocess.run([node, "-e", script, temporary.name])
            if completed.returncode != 0:
                failures.append(f"Node failed to instantiate/call WASM (exit {completed.returncode})")

    if failures:
        for failure in failures:
            print("FAIL:", failure)
        return 1

    print(f"OK: {HTML.relative_to(ROOT)} is self-contained ({HTML.stat().st_size} bytes; WASM {len(wasm)} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
