# uta! Transparent Fallback Stress

This is a failure-mode test skin for the uta! gameplay HUD. Every `uta-*`
texture, including the marker, is a valid fully transparent RGBA PNG.

Expected result: the skin is detected, but critical gameplay information stays
visible through ruleset-owned fallbacks. In particular, target notes, the
playhead, live/reference pitch curves, dynamic lyrics, and scoring feedback must
not disappear.

Build the importable archive from the repository root:

```sh
python3 test-skins/build_transparent_fallback_skin.py
```
