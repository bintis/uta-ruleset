#!/usr/bin/env python3
"""Build the uta! transparent-fallback stress skin deterministically."""

from __future__ import annotations

import binascii
import struct
import zlib
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / "Uta-Transparent-Fallback"
OUTPUT = ROOT / "Uta-Transparent-Fallback.osk"

ASSETS = (
    "curve-live",
    "curve-reference",
    "curve-trail",
    "fault-coverage",
    "fault-high",
    "fault-inaccurate",
    "fault-low",
    "fault-unstable",
    "feedback-bad",
    "feedback-good",
    "feedback-great",
    "feedback-miss",
    "feedback-perfect",
    "grid-major",
    "grid-minor",
    "hud-accent",
    "hud-panel",
    "lyrics-current-underline",
    "lyrics-panel",
    "lyrics-progress-fill",
    "lyrics-reading-marker",
    "lyrics-upcoming-marker",
    "particle-score",
    "particle-sing",
    "pitch-panel",
    "playhead",
    "skin-marker",
    "target-note-freestyle",
    "target-note-golden-freestyle",
    "target-note-golden-rap",
    "target-note-golden-spoken",
    "target-note-golden",
    "target-note-normal",
    "target-note-rap",
    "target-note-spoken",
)


def png_chunk(kind: bytes, data: bytes) -> bytes:
    payload = kind + data
    return struct.pack(">I", len(data)) + payload + struct.pack(">I", binascii.crc32(payload) & 0xFFFFFFFF)


def transparent_png(size: int) -> bytes:
    scanline = b"\0" + (b"\0\0\0\0" * size)
    raw = scanline * size
    return (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
        + png_chunk(b"IDAT", zlib.compress(raw, level=9))
        + png_chunk(b"IEND", b"")
    )


def main() -> None:
    SOURCE.mkdir(parents=True, exist_ok=True)

    for asset in ASSETS:
        (SOURCE / f"uta-{asset}.png").write_bytes(transparent_png(1))
        (SOURCE / f"uta-{asset}@2x.png").write_bytes(transparent_png(2))

    with ZipFile(OUTPUT, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
        for path in sorted(SOURCE.iterdir(), key=lambda value: value.name):
            if path.is_file() and (path.name == "skin.ini" or path.suffix == ".png"):
                archive.write(path, path.name)

    print(f"Built {OUTPUT} with {len(ASSETS) * 2} transparent textures")


if __name__ == "__main__":
    main()
