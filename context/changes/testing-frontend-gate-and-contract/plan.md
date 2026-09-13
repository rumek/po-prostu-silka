# Frontend Gate and API Contract Implementation Plan

## Overview

Rollout Phase 3 of `context/foundation/test-plan.md`. Two risks, one change: an SPA regression reaches
production because no gate runs the frontend specs or lint (#7), and a structural refactor silently
changes a `reason` code the SPA mirrors field-for-field (#6). S-18 and S-19 are both `ready`, and both
move the code these two risks live in — S-18 re-creates every endpoint, S-19 rewrites the error display
that reads the codes.

This change adds no production behaviour. It adds a deploy gate, two local check layers in front of it,
and tests that pin the failure contract on both sides of the API.

## Current State Analysis

**The deploy gate ignores the frontend's own checks.** `.github/workflows/deploy.yml` runs
`npm ci && npm run build` (which is the SPA's only typecheck today), then `dotnet test`, then publish +
migrations + deploy. `npm test` and `npm run quality:check` are run nowhere in CI.

**The specs are green, so the gate can be switched on honestly.** Verified in this session: 44 spec
files / 434 tests pass, `quality:check` (Prettier + angular-eslint) passes. The §2 anti-pattern
("switching the gate on while specs are red, then muting them") does not apply.

**There are no local layers at all.** No `.claude/settings.json` in the repo (only
`settings.local.json`, which holds permissions), no `lefthook.yml`, no Husky, no `.git/hooks` content.
Node 24.15 and npm 11.12 are on the machine; `ng test` accepts `--include <path>` and defaults `--watch`
to false outside a TTY, so a per-edit scoped spec run is feasible. There is no dependency-graph mode
(`vitest related` has no equivalent in `@angular/build:unit-test`), so a per-edit hook can only run a
file's own colocated spec.

**The failure contract is wide and asserted in patches.** The API answers failures as
`{ "reason": "<code>" }` through 16 `*Failure(string Reason)` records; 60 distinct codes appear as
literals. The SPA mirrors them in 11 unions across `core/`. Backend tests read `reason` in 12 suites,
each with its own local `private sealed record FailureBody(string Reason)`.

**Five refusals are built from a variable rather than a literal** — the sites where a refactor can
change the wire value without a literal moving:

| Site | Codes produced |
| --- | --- |
| `src/Application/Auth/AuthEndpoints.cs:372-379` (register) | `invalid_password`, `invalid_email`, `invalid_registration` |
| `src/Application/Auth/AuthEndpoints.cs:563-568` (change password) | `invalid_current_password`, `invalid_new_password` |
| `src/Application/Auth/AuthEndpoints.cs:713-718` (reset password) | `invalid_token`, `invalid_new_password` |
| `src/Application/Members/MemberAdminEndpoints.cs:887` (member contact) | the five `ContactDetails` codes |
| `src/Application/Members/ProfileEndpoints.cs:81` (profile contact) | the same five |

## Desired End State

A failing SPA spec or a lint error blocks the deploy. A formatting or lint mistake in an edited SPA file
comes back to the agent in the same session, and a commit that skipped the agent is refused. Every
`reason` code the SPA maps and that can be reached over HTTP is asserted over HTTP with its status, and
each SPA failure table's reason list is compiler-enforced exhaustive rather than hand-copied into its
spec.

Verify by: `npm test` and `npm run quality:check` green from `src/app/`; `dotnet test` green from the
repo root; each mutation named in a phase's Success Criteria turns its tests red and reverting turns
them green.

### Key Discoveries:

- `deploy.yml:26-31` is one step doing `npm ci` and `npm run build`; the new checks belong between them
  and after them, not in a separate job (dependencies are already installed and cached there).
- `ng test --include` "has special handling for … file paths (includes the corresponding test file if
  one exists)" — so the hook can pass the edited `.ts` path directly.
- The reason-table convention already exists twice: `core/scheduling/booking-failure.ts` and
  `class-failure.ts` key a `Record<Union, string>` so a new code fails the BUILD, and their specs
  hand-list the codes with a count assertion. `core/admin/membership-pass-failure.ts` follows the table
  convention but has no spec.
- `ClassFailure` deliberately excludes `invalid_range` — it is a read-path refusal modelled as
  `ScheduleReadFailure` (`core/scheduling/class.models.ts:143`). A parity test that ignores this
  produces false red.
- `LoginFailureReason` still carries `pending_approval`, which the API has not produced since S-01.
  Both sides already document it as unreachable (`AuthEndpoints.cs:42`, `auth.models.ts:63`), so this is
  a note, not a discovery.
- Test suites own their routes (`test-plan.md` §6.1, §6.2). Contract assertions therefore extend the
  owning suite; the cross-route rule lives in the cookbook, not in a new god-suite.

## What We're NOT Doing

- No production code changes. Drift found while writing tests is recorded, not fixed (§6.6 note or a
  roadmap question) — the Phase 1 and Phase 2 precedent (roadmap Q7, Q8).
- Not adding message tables for exercise / training-plan / class-type failures. That is S-19's CS-05.
- Not removing `pending_approval` from `LoginFailureReason`, and not touching `AccountStatus.Pending`.
- No pre-push hook and no backend post-edit hook: `dotnet test` starts a SQL Server container
  (30-60 s), which breaks the "per-edit checks stay fast" rule.
- No e2e, no Playwright, no browser scenarios (test-plan §4 and Lesson 4's boundary).
- Not authoring a new CI workflow — two steps are added to the one that exists.
- Not asserting `invalid_registration`: it is Identity's unrecognised-error fallback and cannot be
  provoked deterministically from the API.

## Implementation Approach

Gate first, then the material the gate protects. Phase 1 lands the CI steps and both local layers while
the suite is green, so every later commit in this change is already passing through them. Phase 2 pins
the backend half of the contract inside the suites that own the routes. Phase 3 gives the SPA half its
own compiler-enforced oracle and writes the cookbook entries the next person will read.

## Critical Implementation Details

**Ordering inside the CI job.** The new steps must sit before `dotnet test` and before `Publish API`,
so a frontend failure aborts the run while the previous artifact still serves and the production
database is untouched — the same reasoning the existing comment at `deploy.yml:42-45` gives for the
backend tests. `quality:check` (seconds) goes before `npm run build`; `npm test` after it, since the
spec run builds the app anyway.

**The hook must not run the SPA spec build on every edit.** A spec run costs ~20 s of bundling before
the first test. Scope the spec half of the hook to `src/app/src/app/core/**` and `shared/**` — the
hot-spot dirs risk #7 names — and let lint/format cover everything else.

## Phase 1: The gate and the two local layers

### Overview

`npm test` and `npm run quality:check` block the deploy; a per-edit hook feeds format/lint/spec failures
back to the agent; a pre-commit hook catches what bypassed the agent.

### Changes Required:

#### 1. Deploy workflow

**File**: `.github/workflows/deploy.yml`

**Intent**: Make the frontend's own checks part of the gate that already protects the deploy, without
changing job structure, ordering guarantees or the Node/dotnet setup.

**Contract**: Split the existing `Build Angular (static)` step so `npm ci` keeps its place, then add two
steps with `working-directory: src/app`: `npm run quality:check` before the build, and `npm test` after
it. Both must precede the existing `Run tests` step. Name them so a red run says which half failed.
Carry a comment saying the specs were green when the gate was switched on (test-plan §2's anti-pattern).

#### 2. Per-edit agent hook

**File**: `.claude/settings.json` (new, committed — distinct from the existing `settings.local.json`,
which stays untouched)

**Intent**: A `PostToolUse` hook on `Write|Edit` so a formatting slip, a lint error or a broken
colocated spec reaches the agent in the next turn instead of at commit time.

**Contract**: `hooks.PostToolUse[0].matcher` is `Write|Edit`; the single handler is a `command` invoking
the script below. Exit 2 on failure so stdout flows into the agent's context; exit 0 when the edited
path is irrelevant. `~/.claude/settings.json` is not touched — this hook belongs to the repo.

#### 3. The hook script

**File**: `.claude/hooks/post-edit-spa.sh` (new)

**Intent**: Decide from the edited path whether anything needs to run, then run the cheapest useful
check. Keeping the logic in a script rather than inline JSON keeps the settings file reviewable and lets
the script be run by hand.

**Contract**: Reads the hook payload on stdin and extracts `tool_input.file_path` **with `node -e`, not
`jq`** — `jq` is not guaranteed on this machine, `node` is required by the repo. Then:

- path not under `src/app/` → exit 0, silent.
- `*.ts`, `*.html`, `*.scss` under `src/app/` → `npx prettier --check <file>` and `npx eslint <file>`.
- additionally, a `*.ts` (not `*.spec.ts`) under `src/app/src/app/core/` or `.../shared/` that has a
  sibling `*.spec.ts` → `npx ng test --include <file>`; a `*.spec.ts` in those dirs runs itself.
- any check failing → print the tool's own output and exit 2.

All commands run with `src/app` as cwd. Paths arrive absolute; make them relative to `src/app` before
passing them on.

**Adapted during implementation.** The contract above says "path not under `src/app/` → exit 0,
silent", and an unreadable payload fell into that same branch — so a hook that could not parse its
input reported success, indistinguishable from a clean file. Verification surfaced this (a malformed
test payload parsed as nothing and every check was skipped). The script now separates the two: node
exits 3 when there is no `tool_input.file_path`, and the script prints a one-line diagnostic to stderr
and exits 0. Still non-blocking — a hook bug must not stall the agent — but never silent.

#### 4. Pre-commit hook

**File**: `lefthook.yml` (new, repo root) and `src/app/package.json`

**Intent**: Catch SPA edits that never passed through the agent — a manual fix, a merge, an editor save.

**Contract**: `pre-commit.commands` runs Prettier in `--check` mode and `eslint` over `{staged_files}`
with `root: src/app` and `glob: "*.{ts,html,scss}"`. Check, never `--write`: a hook that rewrites staged
content changes what is being committed. `lefthook` goes into `src/app` devDependencies (its npm package
installs the git hooks in a postinstall script). Because `package.json` lives in `src/app` while
`lefthook.yml` and `.git` live at the repo root, the implementer must confirm the install resolved the
git root — see Manual Verification.

### Success Criteria:

#### Automated Verification:

- SPA specs pass from `src/app`: `npm test`
- SPA lint and format pass from `src/app`: `npm run quality:check`
- Backend tests still pass from the repo root: `dotnet test`
- The workflow file parses: `node -e "require('fs').readFileSync('.github/workflows/deploy.yml','utf8')"` plus a YAML lint of choice, or `gh workflow view` once pushed
- `.claude/settings.json` is valid JSON: `node -e "JSON.parse(require('fs').readFileSync('.claude/settings.json','utf8'))"`

#### Manual Verification:

- Introduce a formatting error in a `core/` spec file, save it through Edit: the hook reports it and the
  agent sees the message; revert and the hook goes quiet.
- Break an assertion in a `core/` colocated spec through Edit: the hook runs that one spec file and
  fails; confirm no full-suite run happened.
- Edit a file outside `src/app/` (e.g. a `.cs` file): the hook exits silently and adds no delay.
- `git commit` with a badly formatted staged SPA file is refused; `lefthook` ran from the repo root
  against the `src/app` root.

**Implementation Note**: After completing this phase and all automated verification passes, pause for
manual confirmation before proceeding.

---

## Phase 2: The backend half of the reason contract

### Overview

Every `reason` the SPA maps and that can be reached over HTTP is asserted over HTTP, with its status
code, in the suite that owns the route — with the five variable-built sites as the priority, since
nothing pins them today.

### Changes Required:

#### 1. Establish what is already asserted

**File**: none — a read step

**Intent**: Avoid writing a second assertion for a code an existing suite already pins, and avoid
claiming coverage that is not there.

**Contract**: For each SPA union, list its codes against the assertions already present in
`tests/po-prostu-silka.Tests/`. Record the result as a short table in this plan under this phase before
writing tests. Codes verified as already asserted in this session: `no_valid_pass`, `no_entries_left`,
`member_blocked`, `already_booked`, `class_full`, `class_started`, `class_cancelled`.

#### 2. Auth and profile refusals — the variable-built sites

**Files**: `tests/po-prostu-silka.Tests/PasswordEndpointTests.cs`,
`tests/po-prostu-silka.Tests/RegisterEndpointTests.cs`,
`tests/po-prostu-silka.Tests/ProfileEndpointTests.cs`,
`tests/po-prostu-silka.Tests/MemberAdminEndpointTests.cs`

**Intent**: Pin status + `reason` for the refusals whose value is chosen by a ternary over Identity
error codes or by `ContactDetails.TryCreate`, so a refactor that rewrites those branches cannot change
the wire value silently.

**Contract**: For each: a wrong current password answers 400 `invalid_current_password`; a new password
the policy refuses answers 400 `invalid_new_password`; a reset with a malformed or reused token answers
400 `invalid_token`, and a reset whose new password is refused answers 400 `invalid_new_password`; a
registration with a bad code answers `invalid_member_code` / `unknown_member_code` as the format-vs-
lookup split in `RegisterFailureReason` describes; a contact-detail save with each of the five invalid
fields answers 400 with that field's code, on both the profile route and the member-admin route.

**Oracle rule** (test-plan §6.1): the expected code is a literal written from the rule and the SPA union
it mirrors, never read from `ContactDetails` or from a production constant. Name the mirrored TS file in
a comment on each new theory or test.

#### 3. Class, class-type, exercise and training-plan refusals the SPA maps

**Files**: `tests/po-prostu-silka.Tests/ClassEndpointTests.cs`,
`ClassTypeEndpointTests.cs`, `ExerciseEndpointTests.cs`, `TrainingPlanEndpointTests.cs`,
`MembershipPassEndpointTests.cs`

**Intent**: Close the gaps found in step 1 for the unions whose files S-18 moves — `class.models.ts` has
16 codes and `ClassEndpoints.cs` is the largest file being split.

**Contract**: Extend the existing `[Theory]` blocks that already assert `reason` where one exists
(`Invalid_request_is_refused_with_its_reason`, `Create_enforces_the_numeric_bounds`) rather than adding
parallel tests. Skip codes that require losing an optimistic race (`conflict`, `member_changed`) — §6.1
already rules that a race test which passes either way is worse than none; assert those as
stamp-rotation elsewhere or leave them, noting the choice. `invalid_range` is asserted as the read-path
refusal it is, not as a `ClassFailure`.

### Success Criteria:

#### Automated Verification:

- Backend tests pass from the repo root: `dotnet test`
- The scoped suites pass: `dotnet test --filter FullyQualifiedName~PasswordEndpointTests`, likewise for `RegisterEndpointTests`, `ProfileEndpointTests`, `ClassEndpointTests`

#### Manual Verification:

- Change one asserted literal in `src/` (e.g. `"invalid_current_password"` → `"wrong_password"`): the
  new test turns red; revert and it is green.
- Swap the two branches of a variable-built ternary (register's password/email branches): a new test
  turns red — this is the mutation the phase exists for.
- Confirm Docker is running and the Testcontainers SQL Server comes up; the first run takes ~30-60 s.

**Implementation Note**: Pause for manual confirmation before Phase 3.

---

## Phase 3: The SPA half, and the cookbook

### Overview

Each failure table's reason list becomes compiler-enforced and shared with its spec; the karnet table
gets the spec the other two already have; the test plan records how to do both.

### Changes Required:

#### 1. Exhaustive reason lists beside each union

**Files**: `src/app/src/app/core/scheduling/booking.models.ts`, `class.models.ts`,
`src/app/src/app/core/admin/member-admin.models.ts`

**Intent**: The specs currently hand-copy the union's codes into a `REASONS` array guarded by a count
assertion — a second oracle that goes stale silently. Export one list per union that the compiler
verifies is complete.

**Contract**: For each union, export a `readonly` array derived from an object literal
`satisfies Record<Union['reason'], true>`, so omitting a code is a build error rather than a stale test.
Name them after the union (`BOOKING_FAILURE_REASONS`, `CLASS_FAILURE_REASONS`,
`MEMBERSHIP_PASS_FAILURE_REASONS`). This is the construct the count assertions were standing in for, so
it is the one place a snippet is warranted:

```ts
export const BOOKING_FAILURE_REASONS = Object.keys({
  class_cancelled: true,
  // … every member of the union
} satisfies Record<BookingFailure['reason'], true>) as readonly BookingFailure['reason'][];
```

#### 2. Specs consume the list; the karnet table gets one

**Files**: `src/app/src/app/core/scheduling/booking-failure.spec.ts`, `class-failure.spec.ts`,
`src/app/src/app/core/admin/membership-pass-failure.spec.ts` (new)

**Intent**: Keep what the two existing specs prove (a code with no message, an unknown code rendering as
nothing) while removing the hand-copied list, and give the karnet table the same proof.

**Contract**: The two existing specs import their list and drop both the local `REASONS` array and the
`expect(REASONS.length).toBe(n)` assertion — the `satisfies` clause now owns exhaustiveness. The new
spec follows their shape: a message for every reason, the fallback for an unknown / `undefined` / `null`
reason, and the S-16 rule that `member_blocked`, `overlapping_pass` and `has_active_bookings` do not
read alike.

#### 3. Test plan cookbook and status

**File**: `context/foundation/test-plan.md`

**Intent**: Fill the two sub-sections this phase was the placeholder for, and flip the gates that are
now wired.

**Contract**: Write §6.3 (adding an SPA spec the deploy gate enforces: where specs live, the table +
`satisfies` convention, how to run one file with `--include`, the per-edit hook's scope) and §6.4
(pinning an API failure contract: status + `reason` over HTTP in the suite that owns the route, literal
oracles, name the mirrored TS union, don't test race-only codes). In §3, set Phase 3's Status to
`complete`. In §5, move SPA specs and SPA lint to `required (wired)` and the post-edit hook to
`recommended (wired)`. Append a §6.6 Phase 3 note recording: the specs were green before the gate went
on; `pending_approval` is dead in `LoginFailureReason` and already documented as such on both sides;
`invalid_registration` is unassertable; race-only codes were left unpinned. Bump the "Last updated"
line.

### Success Criteria:

#### Automated Verification:

- SPA specs pass from `src/app`: `npm test`
- SPA lint and format pass from `src/app`: `npm run quality:check`
- The three failure specs pass on their own: `npx ng test --include src/app/core/scheduling/booking-failure.spec.ts` and the other two
- Backend tests still pass from the repo root: `dotnet test`

#### Manual Verification:

- Add a code to `MembershipPassFailure['reason']` without touching anything else: the build fails in
  both `membership-pass-failure.ts` and the new exported list; revert.
- Delete one message from `MESSAGES` in `membership-pass-failure.ts`: the build fails; make it fall
  through instead (cast the Record to `Partial`) and the new spec fails. Revert both.
- Read §6.3 and §6.4 as someone who has not seen this change: they say where to put a new spec and how
  to pin a new refusal, without needing the plan.

---

## Testing Strategy

### Unit Tests:

- SPA: one spec per failure table — a message for every code, a fallback for an unknown code, and the
  message pairs the product requires to stay distinct.

### Integration Tests:

- Backend: status + `reason` over HTTP for each SPA-mapped code reachable without a race, in the suite
  that owns the route, arranged through fixture helpers rather than through the API under test.

### Manual Testing Steps:

1. Run the mutation checks listed per phase; each named test goes red, then green on revert.
2. Edit an SPA `core/` file through the agent and confirm the hook's feedback arrives in-session.
3. Commit a badly formatted staged SPA file and confirm the refusal.
4. After the first push to `main`, confirm the two new CI steps ran and that a deliberately red spec
   would abort the run before `Publish API` (read the step order in the run log; do not push a red
   spec).

## Performance Considerations

The CI job grows by roughly 70-90 s (a `quality:check` in seconds, a spec run of ~62 s locally plus its
~20 s build). The per-edit hook is the one that can hurt the agent loop: lint and format on one file are
sub-second, but a spec run pays the bundling cost, which is why it is scoped to `core/` and `shared/`.
If that proves too slow in practice, the spec half moves to pre-commit and the hook keeps lint/format —
record that as an adaptation in this plan if it happens.

## Migration Notes

None — no schema, no data, no runtime behaviour. The one operational note: after this lands, every
contributor needs `lefthook` installed (it happens on `npm install` in `src/app`), and a clone that
skips that install simply has no pre-commit hook, which is why CI is the gate that matters.

## References

- Test plan: `context/foundation/test-plan.md` (§2 risks #6/#7, §4 stack, §5 gates, §6.3/§6.4 to fill)
- Prior phases: `context/changes/testing-booking-invariants/plan.md`,
  `context/changes/testing-access-surface/plan.md`
- Reason-table convention: `src/app/src/app/core/scheduling/class-failure.ts`,
  `core/scheduling/booking-failure.spec.ts`
- Variable-built refusals: `src/Application/Auth/AuthEndpoints.cs:372`, `:563`, `:713`;
  `src/Application/Members/MemberAdminEndpoints.cs:887`; `src/Application/Members/ProfileEndpoints.cs:81`
- CI ordering rationale: `.github/workflows/deploy.yml:42-45`
- Lefthook config reference: `root`, `glob` and `{staged_files}` per Lefthook's own docs (queried
  2026-09-13)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename
> step titles. See `references/progress-format.md`.

### Phase 1: The gate and the two local layers

#### Automated

- [x] 1.1 SPA specs pass (`npm test`)
- [x] 1.2 SPA lint and format pass (`npm run quality:check`)
- [x] 1.3 Backend tests pass (`dotnet test`)
- [x] 1.4 Workflow file parses
- [x] 1.5 `.claude/settings.json` is valid JSON

#### Manual

- [ ] 1.6 Formatting error in a `core/` spec surfaces through the hook, then goes quiet on revert
- [ ] 1.7 Broken `core/` spec runs only that file through the hook
- [ ] 1.8 Edit outside `src/app/` exits silently
- [ ] 1.9 `git commit` refuses a badly formatted staged SPA file

### Phase 2: The backend half of the reason contract

#### Automated

- [ ] 2.1 Backend tests pass (`dotnet test`)
- [ ] 2.2 Scoped suites pass (`--filter` per suite)

#### Manual

- [ ] 2.3 Changing one asserted literal in `src/` turns the new test red
- [ ] 2.4 Swapping a variable-built ternary's branches turns a new test red
- [ ] 2.5 Testcontainers SQL Server comes up

### Phase 3: The SPA half, and the cookbook

#### Automated

- [ ] 3.1 SPA specs pass (`npm test`)
- [ ] 3.2 SPA lint and format pass (`npm run quality:check`)
- [ ] 3.3 The three failure specs pass individually via `--include`
- [ ] 3.4 Backend tests pass (`dotnet test`)

#### Manual

- [ ] 3.5 Adding a union member fails the build in both table and list
- [ ] 3.6 A fallen-through message fails the new karnet spec
- [ ] 3.7 §6.3 and §6.4 stand on their own for a new reader
