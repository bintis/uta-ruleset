# Local remote security model

The remote listener is disabled by default and is created only after an explicit desktop action. It is owned by one gameplay instance; disposing gameplay stops the listener, disconnects sockets, clears pending tickets and revokes reconnect sessions.

## Trust boundary

The phone is untrusted until it presents either a 90-second, single-use pairing ticket or a session reconnect secret. Credentials are generated with `RandomNumberGenerator`, stored only as SHA-256 hashes in the desktop process, and are never persisted to the ruleset configuration. The browser keeps reconnect material in `sessionStorage`, not durable local storage.

Only loopback, RFC1918 IPv4 and link-local IPv4 clients are accepted. Host must match a bound interface and the listener port. Browser Origin, when present, must be the same HTTP origin and port. The client HTML has a restrictive CSP, no third-party resources and no microphone/camera/geolocation permission.

## Roles and revocation

A controller can mutate gameplay. Pairing a new controller revokes the previous controller credential. Spectators are read-only except for ping/disconnect. Desktop controls can revoke all sessions. A session is also revoked by the client's explicit disconnect command and all credentials disappear when gameplay exits.

## Abuse resistance

Commands are strict JSON with a 32 KiB maximum and depth 12. Unknown commands, non-finite values and values outside desktop bounds are rejected. Per-session sequences are strictly increasing, preventing replay and out-of-order mutation. A monotonic token bucket permits a short burst while limiting sustained command load. The service caps simultaneous clients and sends server-authoritative snapshots after rapid or conflicting changes.

## Operational limitations

This is local HTTP/WebSocket, not TLS. It is intended only for a trusted private LAN. Do not expose the port through router forwarding, public Wi-Fi port mapping or a reverse proxy. On Windows, binding a non-loopback `HttpListener` prefix may require an administrator-created URL ACL; the UI reports the bind error rather than silently falling back to a wider listener.
