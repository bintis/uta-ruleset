#!/usr/bin/env python3
"""Build the installable ruleset zip: DLL, BASSFLAC plugin, and licence text."""

from __future__ import annotations

import argparse
import zipfile
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj"
OUTPUT_DIR = ROOT / "osu.Game.Rulesets.Uta/bin/Release/net8.0"
REQUIRED = (
    "osu.Game.Rulesets.Uta.dll",
    "libbassflac.so",
    "BASSFLAC.txt",
)


def ruleset_version() -> str:
    version = ElementTree.parse(PROJECT).findtext(".//{*}Version")
    if not version:
        raise SystemExit("ruleset Version is missing from the csproj")
    return version.strip()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, default=ROOT)
    args = parser.parse_args()

    missing = [name for name in REQUIRED if not (OUTPUT_DIR / name).is_file()]
    if missing:
        raise SystemExit(f"Release output is missing: {', '.join(missing)}")

    version = ruleset_version()
    zip_path = args.output_dir / f"uta-ruleset-v{version}.zip"
    args.output_dir.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name in REQUIRED:
            archive.write(OUTPUT_DIR / name, arcname=name)

    print(f"wrote {zip_path.relative_to(ROOT)} ({zip_path.stat().st_size} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
