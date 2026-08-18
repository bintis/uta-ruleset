# Device and integration acceptance matrix

Run these after CI passes on the target osu! build.

| Area | Required checks | Pass condition |
|---|---|---|
| Pairing | Android Chrome, iOS Safari; controller and spectator; ticket expiry; second redemption | One redemption only; spectator cannot mutate; expired/replayed credentials rejected |
| Reconnect | Wi-Fi off/on, tab background/foreground, rapid reconnect | Same session resumes; state converges; no duplicate controller |
| Network | loopback, normal private LAN, AP isolation, public adapter, malformed Host/Origin | Only bound private interfaces work; public/cross-origin requests fail |
| Command pressure | hold sliders, multi-touch, malformed/oversized/deep JSON | Gameplay thread remains responsive; rate/size/depth limits trigger |
| Exit/retry | quit, retry, fail, switch ruleset while connected | Port closes and all tickets/sessions become unusable |
| Recording | 2-hour take, 200 phrase retries, cancel during write, full disk, device switch | bounded memory; no lost final block; every rented buffer and stream released |
| Scoring matrix | Transpose -6/0/+6 × OCT × 0.5/1/1.5 rate × latency bounds × A-B/phrase loops | deterministic result for fixed frames; option changes create a new non-comparable epoch |
| Auto | full chart with rests and seeks | formal score/results complete; silent sections remain unvoiced |
| Skin | default, missing elements, extreme colours/weights, missing glyphs | safe fallback; contrast/readability retained; no crash |
| Video | no video, supported video, decoder failure; pause/seek/loop/rate; offset bounds | video follows gameplay clock; failure does not stop audio/gameplay |
| Accessibility | narrow phone, touch, keyboard/controller, colour-blind filters, reduced motion | every control reachable and labelled; reduced-motion disables optional effects |
