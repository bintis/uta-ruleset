# Input and accessibility behaviour

## Keyboard and controller

All uta! actions are regular lazer ruleset bindings and can be remapped in
lazer's input settings. Keyboard defaults are listed by the key-binding screen.
Controller defaults use joystick buttons 1–8 for quick settings, loop A/B,
clear loop, phrase previous/next, retry and phrase looping respectively. They
do not replace any player remapping, so device-specific layouts can be adjusted
in lazer's input settings.

## Touch and narrow layouts

The desktop quick settings, Practice HUD and queue use lazer's standard focus
and pointer controls. The paired phone client uses tap, drag and swipe for its
library, playback and queue screens. `UtaHudLayoutCoordinator` selects Wide,
Standard, Compact or Narrow density at 1280, 840 and 560-pixel boundaries;
lyrics avoid practice controls and protected safe areas in every layout.

## Visual accessibility

The default accessible palette distinguishes target, reference, live voice and
feedback by cool/warm colour, luminance, line treatment and labels rather than
colour alone. Semantic fallbacks remain visible when an active skin omits or
makes textures transparent. **Reduced motion** removes optional singing/scoring
particles and lyric-token pulses while retaining all target, score and fault
cues.

Regression coverage checks all density boundaries, independent HUD visibility,
practice avoidance, accessible palette contrast/cues and reduced-motion
particle/pulse suppression (`UtaReleaseRegressionTests`).
