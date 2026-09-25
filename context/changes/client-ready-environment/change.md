---
change_id: client-ready-environment
title: The environment is fit to show to a client, and tells the owner when it is not
status: implementing
created: 2026-09-25
updated: 2026-09-25
---

## Notes

Roadmap item S-28 (M-9 `client-ready-launch`), scope anchors GL-01–GL-06 in the M-9 charter of
`context/foundation/roadmap.md`.

User's decisions (2026-09-25, Polish, recorded in English):

- One environment still. It stays `Staging` with the S-24 test club, and each client gets a
  real-address account added by hand. The `TestDataSeed:Reset` hazard is guarded rather than removed.
- No domain is bought yet. The custom sender domain is a prepared phase, blocked until one is
  bought, and the slice is not `done` until that phase lands.
- A throttled e-mail lane (the managed domain's hard 10/hour cap) must not page the owner the way a
  dead worker does: two signals, two thresholds.
- `main` is protected: changes arrive through a pull request with green checks, no required
  reviewer, no admin bypass.
- Application Insights with a daily cap receives errors and telemetry.
- CI credentials (publish profile, SDK-auth secret), SQL auth and the `AllowAzureServices` firewall
  rule stay as they are and are recorded as accepted risks until the second environment exists.
- CSP is enforced from the start (`script-src 'self'`), after every persona's screens are clicked
  through.
- Rehearse the artifact rollback and a point-in-time restore to a NEW database, once each.
- Budget: $25/month on `pps-rg`; alerts go by e-mail to the owner.
