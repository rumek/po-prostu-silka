# Access Surface — Plan Brief

> Full plan: `context/changes/testing-access-surface/plan.md`
> Research: `context/changes/testing-access-surface/research.md`

## What & Why

Rollout Phase 2 of `context/foundation/test-plan.md`: retired doors stay shut (#3) and nobody reaches what
is not theirs (#4). The scenarios are mostly tested already; what is missing is a guarantee that every
endpoint is authorized by the right policy — and roadmap S-18 is about to re-create every route group.

## Starting Point

There is no authorization fallback policy, so an endpoint is anonymous unless its group says otherwise.
Refusals are checked by hand-listed theories in six suites; the member and karnet admin groups — which
grant roles, issue codes and block people — are probed on a single route.

## Desired End State

One metadata test turns red if any API route is anonymous outside login/register/reset, any admin route
is reachable by a trainer beyond the five routes the product gives trainers, or any trainer route loses
its policy. Every member-admin and karnet route refuses anonymous, member and trainer over HTTP. Retired
approval routes are asserted gone. The trainer picker is proven free of member email.

## Key Decisions Made

| Decision | Choice | Why | Source |
|---|---|---|---|
| Inventory strength | Anonymous allowlist + prefix rules + five named trainer exceptions | Catches both a dropped policy and groups merged the wrong way in S-18; exceptions come from S-16/S-11 decisions | Plan |
| Full route→policy map | Rejected | A snapshot of today blesses today's mistakes | Plan / test plan anti-pattern |
| Why HTTP theories too | Metadata cannot see a policy redefined to admit trainers | Two layers, two failure modes | Research |
| Retired approval routes | Test them; relax the exact-405 assertion | Interview Q2; GETs answer the SPA shell, so assert "not JSON" | Plan |
| Roster emails to trainers | Roadmap Open Question 8, no test | Pinning it could cement a privacy breach | Plan |
| Rate-limit bypass | No test | Spoofable by accepted design; the cap is tested | Research |
| SPA | Only fix a stale spec title | All guards already specced | Research |

## Scope

**In scope:** inventory test; refusal theories for 15 admin routes; retired approval routes; relaxed 405;
picker privacy test; spec title; roadmap Q8; test-plan §6.2.

**Out of scope:** production changes (fallback policy, roster payload); re-testing covered cases.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Authorization inventory | One metadata test, three mutations red | Pattern normalization (trailing slash, shared prefixes) |
| 2. Refusals + retired doors | ~45 theory cases, 2 retired-route facts, relaxed 405, spec title | SPA-shell 200 misread as success |
| 3. Picker privacy + docs | Picker test, roadmap Q8, §6.2 | — |

**Prerequisites:** Docker running; Node 22+ for the SPA spec run.
**Estimated effort:** one session.

## Open Risks & Assumptions

- Assumes endpoint patterns surface as the group prefix plus the route template; Phase 1 normalizes the
  trailing slash.
- `roadmap.md` may again carry unrelated edits at commit time.

## Success Criteria (Summary)

- Each mutation named in the plan turns its tests red; reverting turns them green.
- `dotnet test` and `npm test` are green at the end.
