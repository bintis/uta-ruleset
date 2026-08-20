# uta! skin contract

uta! reads its gameplay presentation from the active osu!lazer skin. It does
not install or select a second ruleset-specific skin package. A legacy `.osk`
opts into the uta! contract by including `uta-skin-marker.png`; without that
marker, files whose names happen to start with `uta-` are ignored.

Normal lazer `@2x` selection applies. Every texture is optional. The ruleset
always draws an accessible semantic fallback underneath custom textures, so a
missing or fully transparent decorative asset cannot hide a target note,
curve, playhead, lyric, or textual judgement.

## Texture names

### Pitch HUD

- `uta-pitch-panel`
- `uta-grid-major`, `uta-grid-minor`
- `uta-playhead`
- `uta-curve-reference`, `uta-curve-live`, `uta-curve-trail`
- `uta-target-note-normal`, `uta-target-note-golden`
- `uta-target-note-freestyle`, `uta-target-note-rap`, `uta-target-note-spoken`
- `uta-target-note-golden-freestyle`, `uta-target-note-golden-rap`,
  `uta-target-note-golden-spoken`

Pitch textures are stretched to the primitive geometry. Target textures may
provide a distinctive interior while the ruleset retains its outline and
critical centre cue. Completed notes keep their grade tint.

### Lyrics

- `uta-lyrics-panel`
- `uta-lyrics-current-underline`
- `uta-lyrics-progress-fill`
- `uta-lyrics-reading-marker`
- `uta-lyrics-upcoming-marker`

Text and timing remain ruleset-owned. Textures decorate the configured
underline, fill or marker progress presentation and never replace lyric text.

### Score feedback and particles

- `uta-hud-panel`, `uta-hud-accent`
- `uta-feedback-perfect`, `uta-feedback-great`, `uta-feedback-good`
- `uta-feedback-bad`, `uta-feedback-miss`
- `uta-fault-high`, `uta-fault-low`, `uta-fault-unstable`
- `uta-fault-coverage`, `uta-fault-inaccurate`
- `uta-particle-sing`, `uta-particle-score`

Feedback icons accompany the textual judgement and fault explanation. Singing
and scoring particles use fixed-capacity pools, obey the particle-intensity
setting, and are disabled by reduced motion.

## Native skin-provider lookups

Native skin providers can supply `UtaTargetNoteLookup`, `UtaCurveLookup`,
`UtaGridLookup`, `UtaLyricsDecorationLookup`, `UtaScoringFeedbackLookup` and
`UtaParticleLookup`. They can also provide `UtaSkinConfigurationLookup` values
for semantic colours, line weights, target dimensions, lyric typography,
note spacing and animation intensity. Unsafe transparent colours and
out-of-range numeric values are replaced or clamped by the ruleset.

## Fallback acceptance skin

`test-skins/Uta-Transparent-Fallback` contains transparent versions of every
asset. Build it with:

```sh
python3 test-skins/build_transparent_fallback_skin.py
```

With that skin active, all gameplay-critical fallback cues and text must remain
visible while both particle layers remain bounded and reduced-motion aware.
