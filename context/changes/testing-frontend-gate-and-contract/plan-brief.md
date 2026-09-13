# Frontend Gate and API Contract — Plan Brief

> Full plan: `context/changes/testing-frontend-gate-and-contract/plan.md`

## What & Why

Rollout Phase 3 of `context/foundation/test-plan.md`: an SPA regression reaches production because no
gate runs the frontend specs or lint (#7), and a structural refactor silently changes a `reason` code the
SPA mirrors field-for-field (#6). S-18 and S-19 are both `ready` and both move exactly that code.

## Starting Point

CI builds the SPA (its only typecheck) and gates on `dotnet test`; `npm test` and `quality:check` run
nowhere. There are no git hooks and no agent hooks. The API answers failures through 16 `*Failure`
records with 60 distinct `reason` literals, mirrored by 11 SPA unions; backend tests assert `reason` in
patches, and five refusals build their code from a variable rather than a literal.

## Desired End State

A failing spec or a lint error blocks the deploy. A format, lint or colocated-spec failure in an edited
SPA file comes back to the agent in-session, and a commit that skipped the agent is refused. Every
SPA-mapped `reason` reachable over HTTP is asserted with its status, and each failure table's reason list
is compiler-enforced instead of hand-copied into its spec.

## Key Decisions Made

| Decision | Choice | Why | Source |
|---|---|---|---|
| CI gate | Two steps in the existing `deploy.yml`, before `dotnet test` | Phase 3's goal is unreachable without it; specs verified green first, so no muting | Plan |
| Non-blocking CI job | Rejected | A gate that does not block is the §2 anti-pattern | Plan |
| Local layers | Per-edit `PostToolUse` hook + lefthook pre-commit | The only layer that feeds the agent, plus one that catches edits bypassing it | Plan |
| Pre-push / backend hook | Rejected | `dotnet test` starts a SQL container (30-60 s) — breaks "per-edit stays fast" | Plan |
| Contract strength | Pin where the SPA reads it, not the whole 60-code vocabulary | A declared catalogue of everything becomes a snapshot of today, updated reflexively | Plan |
| Priority inside that | The five variable-built refusals first | No literal stands at the return, so a refactor can change the wire value invisibly | Plan |
| Drift found while testing | Recorded, not fixed | Phase 1 and 2 precedent (roadmap Q7, Q8); keeps red unambiguous | Plan |
| Test location | The suite that owns the route, no new god-suite | test-plan §6.1/§6.2 convention | Plan |
| SPA scope | Karnet spec + one exhaustive reason list per union | Retires the hand-copied `REASONS` arrays the count assertions were guarding | Plan |
| New message tables | Out — that is S-19's CS-05 | Would need product copy decisions inside a test change | Plan |

## Scope

**In scope:** two CI steps; `.claude/settings.json` + hook script; `lefthook.yml`; contract assertions in
the owning suites (auth/profile/member first); `satisfies`-backed reason lists; karnet failure spec;
test-plan §6.3, §6.4, §6.6, §3 and §5.

**Out of scope:** production code; removing `pending_approval`; new message tables; pre-push and backend
hooks; e2e; a new workflow.

## Architecture / Approach

Gate first, then the material it protects: the CI steps and both local layers land while the suite is
green, so every later commit in this change already passes through them. Then the backend half of the
contract (HTTP status + `reason`, literal oracles), then the SPA half (compiler-enforced lists) and the
cookbook entries.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Gate + local layers | Two CI steps, per-edit hook, pre-commit hook | `lefthook` installed from `src/app` while `.git` is at the repo root |
| 2. Backend contract | Status + `reason` assertions, variable-built sites first | Duplicating an assertion an existing suite already owns |
| 3. SPA half + cookbook | Karnet spec, exhaustive lists, §6.3/§6.4 | A parity list that forgets `invalid_range` lives in `ScheduleReadFailure` |

**Prerequisites:** Docker running (Testcontainers); Node 22+ (24.15 present).
**Estimated effort:** one session, three phases.

## Open Risks & Assumptions

- The per-edit spec run pays a ~20 s bundling cost; it is scoped to `core/` and `shared/`, and if that
  still drags, the spec half moves to pre-commit (to be recorded as an adaptation in the plan).
- `jq` is not assumed present; the hook script parses its stdin with `node`.
- `context/foundation/test-plan.md` already has uncommitted edits in the working tree; Phase 3 edits the
  same file.
- The CI ordering can only be observed on a real run to `main`; the plan verifies it by reading the run
  log, not by pushing a red spec.

## Success Criteria (Summary)

- A red SPA spec or a lint error aborts the deploy run before the publish step.
- Each mutation named in the plan turns its tests red, and reverting turns them green.
- `dotnet test`, `npm test` and `npm run quality:check` are green at the end.
