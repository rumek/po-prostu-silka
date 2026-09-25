# Client-Ready Environment — Plan Brief

> Full plan: `context/changes/client-ready-environment/plan.md`

## What & Why

Roadmap S-28, the north star of M-9. The owner wants to show the app to prospective clubs as a
fully working product, on the one environment that exists. Today that environment has no alerting
and no security headers, and its recovery path has never been tried. The outbox stall of 2026-09-24
ran for hours behind a `Healthy` `/health`, and was found only because someone was waiting for a push.

## Starting Point

No Application Insights, alert or budget exists in `pps-rg`. `/health` has three outbox signals but
cannot tell a throttled e-mail lane from a stuck one. Responses carry no security headers and expose
`Server: Kestrel`. CI runs only on push to `main` and keeps no publish artifact. Azure SQL has 7-day
PITR, never exercised. The Azure-managed e-mail domain is capped at **10 mails/hour per
subscription, not raisable**.

## Desired End State

Every response carries HSTS, an enforced CSP and the baseline headers, and every persona's screens
still work under it. `/health` degrades on a real failure, and on a throttled mail lane only after
3 h. An availability test, an exception alert and a $25 budget e-mail the owner. `main` accepts only
checked PRs. Rollback and a restore to a new database have each been done once and written down. The
custom sender domain is ready to apply the day a domain is bought.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Environment topology | One environment, still | The user deferred a second one; nothing here may block adding it | Roadmap (GL-01) |
| Demo data | Stays `Staging` + the S-24 test club; real-address accounts added by hand | A demo needs a living club; the Reset hazard is guarded by `/health` instead | Plan |
| E-mail cap | Custom sender domain as a prepared phase, blocked until a domain is bought | The managed domain's 10/h cannot be raised; no domain is owned yet | Plan |
| Alert semantics | Two thresholds: 30 min normally, 3 h while the mail lane is throttled | Page on failures, not on a known provider cap | Plan |
| Change flow | Protected `main`, required PR checks, no reviewer, no admin bypass | A red commit never reaches the URL a client sees | Plan |
| Telemetry | Workspace-based App Insights, 0.1 GB daily cap, Warning+ logs, worker SQL spans dropped | Searchable errors without the bill creep infrastructure.md warns about | Plan |
| CI & SQL credentials | Unchanged; OIDC, Managed Identity and firewall recorded as accepted risks | Not worth risking the only live URL before a second environment exists | Plan |
| CSP | Enforced now, `script-src 'self'`, critical-CSS inlining off | Real XSS protection; every screen is clicked through before merge | Plan |
| Rehearsals | Artifact rollback + PITR to a new DB (no live swap) | The two recoveries most likely needed, without risking live data | Plan |
| Budget & alert recipient | $25/month, owner's e-mail via one action group | Matches the infrastructure.md risk register | Plan |

## Scope

**In scope:** security headers, CSP and a generic 500 body; throttle-aware and reset-aware
`/health`; App Insights, an availability test, an exception alert, a budget; PR checks, branch
protection and a rollback workflow; the rollback and PITR rehearsals; documented e-mail limits and a
demo checklist; the custom sender domain (blocked).

**Out of scope:** a second environment and promotion; OIDC; Managed Identity for SQL; firewall
tightening; a custom domain for the app itself; a live database swap; CSP nonces; slots or scale-out.

## Architecture / Approach

Code first (headers, health), then cloud (App Insights and alerts, which depend on the new health
semantics), then process (the PR gate, rollback), then rehearsal and documentation. Azure resources
are created with `az` commands recorded in `deploy-plan.md`, named so a second environment is a
re-run with other names.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Response hygiene & security headers | Headers, enforced CSP, generic 500, no inline script | A missed screen breaks under CSP on the live URL |
| 2. Health signals | Throttle-aware email age, Reset-armed warning | A throttle wrongly exempting push |
| 3. Telemetry, alerts & budget | App Insights, availability test, exception alert, $25 budget | Worker polling fills the daily cap |
| 4. PR gate & rollback pipeline | `ci.yml`, protected `main`, publish artifact, `rollback.yml` | Required-check names drifting from job names |
| 5. Rehearsals & demo readiness | Timed rollback, PITR to a new DB, demo walk-through, docs | Rehearsal restarts during a demo |
| 6. Custom sender domain (blocked) | Verified domain, 100/h cap, retuned threshold | Waits on a domain purchase |

**Prerequisites:** S-27 done (it is). `gh auth login` by the user before Phase 4. A domain with DNS
access before Phase 6. Check the B1 billing before the free offer ends on 2026-09-29.
**Estimated effort:** ~3–4 sessions for Phases 1–5; Phase 6 is about an hour of work plus DNS
propagation.

## Open Risks & Assumptions

- Until Phase 6, a demo cancellation on a class with more than about 5 real-address members delivers
  mail over hours. The demo checklist works around this; it does not remove the cap.
- `style-src 'unsafe-inline'` stays, because Angular's runtime styles would need SSR nonces.
- The slice is not `done` while Phase 6 is blocked. That was accepted by the user.
- CI and SQL credentials stay long-lived secrets, accepted until the second environment exists.

## Success Criteria (Summary)

- The owner runs a full demo on the live URL, and the e-mail and push arrive.
- A forced failure reaches the owner's inbox; a throttled mail lane does not page within 3 h.
- A bad deploy and a lost row have each been recovered once, by a written procedure.
