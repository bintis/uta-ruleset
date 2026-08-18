#!/usr/bin/env python3
"""Static release-overlay checks that do not replace dotnet test or device tests."""

from __future__ import annotations

import argparse
from pathlib import Path

REQUIRED = {
    "osu.Game.Rulesets.Uta/Remote/UtaRemoteProtocol.cs": ["MAX_MESSAGE_BYTES", "Spectator sessions are read-only"],
    "osu.Game.Rulesets.Uta/Remote/UtaRemoteSecurity.cs": ["DEFAULT_PAIRING_LIFETIME", "FixedTimeEquals", "TryAdvance"],
    "osu.Game.Rulesets.Uta/Remote/UtaRemoteServer.cs": ["HttpListener", "Content-Security-Policy", "RevokeAll"],
    "osu.Game.Rulesets.Uta/Remote/Assets/uta-remote.html": ["WASM_BASE64", "WebSocket", "sessionStorage", "microphoneLatency", "accompanimentLatency", "lyricsLatency", "loopState"],
    "osu.Game.Rulesets.Uta/Configuration/UtaRulesetConfigManager.cs": ["MicrophoneOutputDevice = 5", "BackgroundMusicVolume = 0", "ScoreHudPosition = 22", "Reserved23 = 23"],
    "osu.Game.Rulesets.Uta/Recording/UtaPcmCaptureQueue.cs": ["Interlocked.Exchange", "disposeTask", "ArrayPool<float>.Shared.Return"],
    "osu.Game.Rulesets.Uta/Core/UtaAutoplayFrameFactory.cs": ["UtaPitchFrame", "MidiToFrequency"],
    "osu.Game.Rulesets.Uta/Import/UtaImportDiagnostics.cs": ["capacity = 32", "sanitise"],
    "osu.Game.Rulesets.Uta/Skinning/UtaSkinComponents.cs": ["TargetNote", "LivePitchCurve", "ScoringFeedback"],
    "osu.Game.Rulesets.Uta.Tests/UtaReleaseRegressionTests.cs": ["TestPairingTicketIsSingleUse", "TestPcmQueueCompletion"],
}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = args.repo.resolve()
    failures: list[str] = []
    for relative, markers in REQUIRED.items():
        path = root / relative
        if not path.exists() or path.stat().st_size == 0:
            failures.append(f"missing/empty: {relative}")
            continue
        text = path.read_text(encoding="utf-8")
        for marker in markers:
            if marker not in text:
                failures.append(f"{relative}: marker missing: {marker}")

    skip_parts = {"bin", "obj", "target", "__pycache__", ".git"}
    for path in root.rglob("*"):
        if any(part in skip_parts for part in path.parts):
            continue
        if path.is_file() and path.suffix == ".pyc":
            failures.append(f"generated cache checked in: {path.relative_to(root)}")

    for path in root.rglob("*.cs"):
        if any(part in skip_parts for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8")
        if text.count("{") != text.count("}"):
            failures.append(f"unbalanced braces: {path.relative_to(root)}")
        if "TODO_IMPLEMENTATION" in text or "throw new NotImplementedException" in text:
            failures.append(f"placeholder implementation: {path.relative_to(root)}")

    if failures:
        for failure in failures:
            print("FAIL:", failure)
        return 1
    print(f"OK: static overlay audit passed for {root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
