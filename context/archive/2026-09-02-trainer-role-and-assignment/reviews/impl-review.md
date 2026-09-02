<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Trainer Role and Assignment

- **Plan**: `context/changes/trainer-role-and-assignment/plan.md`
- **Scope**: Full plan (Phase 1 and Phase 2)
- **Date**: 2026-09-02
- **Verdict**: REJECTED (triaged 2026-09-02: 6 fixed, 4 skipped)
- **Findings**: 2 critical, 5 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Note on the verdict: neither critical is a security, data-safety or failing-test issue. Both are
defects in the new menu widget. REJECTED follows the skill's rule (any CRITICAL ⇒ Safety & Quality
FAIL ⇒ REJECTED), not a judgement that anything here is dangerous.

## Findings

### F1 — The menu's keyboard support is largely non-functional

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (and Plan Adherence)
- **Location**: `src/app/src/app/features/admin/members/members.html:109-130`, `members.ts:288-314`
- **Detail**: The `(keydown)="onMenuKeydown($event)"` handler sits on `.row-menu-list`, but the
  trigger button is its **sibling**, not its ancestor. Nothing moves focus into the menu when it
  opens, so after activating the trigger focus stays on the trigger and keydown bubbles to
  `.row-menu` — never reaching the handler. Arrow/Home/End navigation is dead code, and because
  `preventDefault()` lives inside the unreachable handler, ArrowDown just scrolls the page.
  Verified by reading the DOM structure directly.

  Three related gaps compound it: menu items call `closeMenu()`, which removes the focused button
  from the DOM via `@if`, dropping focus to `<body>` (`onEscape` gets this right; activation does
  not); items are native buttons with default `tabindex="0"`, so Tab walks through them, which
  contradicts the `role="menu"` promise of a single tab stop; and nothing closes the menu on focus
  leaving it. The trigger also announces as a bare "Akcje" on every row, with no member name — the
  menu container got `aria-label` with the name, the trigger did not.

  The plan's Phase 2 contract states the menu "must be operable by keyboard (open, arrow between
  entries, `Escape` to close, focus returned to the trigger)". Escape works; the arrow half does
  not. This is why the finding also touches Plan Adherence.

  Root cause of it shipping: `members.spec.ts` covers Escape, outside-click and one-open-at-a-time,
  but **never presses an arrow key** and never asserts where focus lands after activating an item.
- **Fix A ⭐ Recommended**: Move focus into the menu on open and handle the trigger's own keys.
  - Strength: Makes the announced `role="menu"` truthful and delivers the plan's stated contract.
    Follows the standard menu-button pattern (trigger: Down → first item, Up → last item; items:
    roving focus with `tabindex="-1"`; activation and Escape both return focus to the trigger).
  - Tradeoff: Needs an `afterNextRender`/microtask to focus after `@if` renders, plus `focusout`
    dismissal — roughly 30 lines and two new tests.
  - Confidence: HIGH — the failure is verified by reading the DOM, and `onEscape` already contains
    the focus-return idiom to copy.
  - Blind spot: Not verified against a real screen reader; jsdom tests will prove focus movement,
    not announcement quality.
- **Fix B**: Drop `role="menu"` and render the actions as a plain group of buttons.
  - Strength: Removes the mismatch between promise and behaviour at once; native Tab order then
    becomes correct by construction. Matches the flat-button idiom the sibling admin screens
    (`approvals/`, `classes/`) already use.
  - Tradeoff: Abandons the affordance chosen deliberately during planning, and the row goes back to
    carrying every action inline — the density problem the menu was picked to solve.
  - Confidence: MEDIUM — cheap and certain to be correct, but it reverses a decision the user made
    with the tradeoff stated.
  - Blind spot: Whether row density is actually a problem at the club's real member count.
- **Decision**: SKIPPED — user chose not to fix; menu keyboard navigation stays non-functional and the plan's "arrow between entries" contract stays undelivered.

### F2 — The menu's styles reference three CSS variables that do not exist

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/admin/members/members.scss:135-160`
- **Detail**: Verified against `src/app/src/styles.scss`: `--border`, `--surface` and
  `--surface-hover` are undefined; the real tokens are `--line`, `--line-strong`, `--ground`,
  `--section-warm`, `--shadow-card`. The consequence is not cosmetic. `background: var(--surface,
  Canvas)` falls back to the browser's **system** `Canvas` colour, ignoring the app's warm
  `--ground` palette — and on a machine in dark mode that renders a dark panel behind
  `color: inherit` dark ink, i.e. an unreadable menu. The box-shadow is also hardcoded where every
  `.card` uses `var(--shadow-card)`.
  (`--space-1/2/3` and `--radius` *are* defined — an earlier check of mine wrongly flagged them
  because its pattern excluded digits.)
- **Fix**: Use the real tokens — `border-color: var(--line-strong)`, `background: var(--ground)`,
  `box-shadow: var(--shadow-card)`, hover `background: var(--section-warm)`.
- **Decision**: FIXED — real tokens substituted (--line-strong / --ground / --shadow-card / --section-warm) and fallbacks dropped from tokens that do exist.

### F3 — Three comments still assert the admin exclusion this change deleted

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `src/Application/Members/MemberAdminEndpoints.cs:121-122` and `:233-234`;
  `src/app/src/app/core/admin/member-admin.service.ts:31`; `members.spec.ts:124`
- **Detail**: `GetMembersAsync`'s XML doc still says "Admins are not members and never appear here
  — the exclusion is in the query", on the very endpoint that now returns them. `BlockAsync`'s
  comment still justifies its guard with "MemberQuery already excludes admins from the list, so the
  button never renders". The SPA service says "Admins are excluded by the API, not here."

  This is exactly the failure the plan's own Critical Implementation Details section set out to
  prevent — it required rewriting the rationale so a reader learns the protection **moved** rather
  than vanished. That was done in `MemberQuery.cs` and missed everywhere else. Worse: a reader who
  opens `MemberAdminEndpoints.cs` first is told the block guard is belt-and-braces, which is the
  reasoning that would justify simplifying away the only remaining protection.
- **Fix**: Rewrite all four to name `BlockAsync`'s `is_admin` check as the sole boundary and point
  at `MemberQuery`'s note.
- **Decision**: FIXED — all four comments rewritten to name BlockAsync's is_admin check as the sole boundary.

### F4 — The sole admin-block guard has no automated test

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/Application/Members/MemberAdminEndpoints.cs:237-240`
- **Detail**: The change deleted a guard that *was* asserted by a test and replaced it with one that
  is asserted by nothing. The failure mode is a permanent lockout: block the only admin and the club
  loses administrative access to its own application with no route back through the UI. A future
  refactor of `BlockAsync` — reordering the idempotency check above the role check, say — breaks it
  silently with CI green.

  **History matters here.** This test was offered during planning and deliberately scoped out; the
  plan records it as manual step 1.6 and as an entry in Open Risks, and manual verification has
  since been confirmed. The finding is re-surfaced because an independent reviewer flagged it as the
  highest-value gap in the change, not because the earlier decision was wrong. A one-time manual
  check does not survive the next refactor.
- **Fix**: Add `Blocking_an_admin_is_409_and_leaves_the_account_active` to
  `MemberAdminEndpointTests.cs`, asserting reason `is_admin` and that `Status` is still `Active` in
  the database. The fixture already provides `TestUsers.ActiveAdminEmail`; roughly 15 lines.
- **Decision**: SKIPPED — original planning decision upheld; manual step 1.6 and the Open Risks entry remain the only coverage.

### F5 — No test covers the `not_active` refusal on revoke

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `src/Application/Members/MemberAdminEndpoints.cs:392`
- **Detail**: `Granting_trainer_to_a_non_active_account_is_409` is a `[Theory]` covering Pending and
  Blocked for **grant** only. Applying the status guard to revoke as well was Phase 1's adaptation
  #3 — the one asymmetry the plan flagged as not being an obvious mirror — and it is the branch with
  no coverage.
- **Fix**: Extend the existing `[Theory]` with a revoke variant, or add a sibling theory that grants
  first, blocks the account, then asserts revoke returns 409 `not_active`.
- **Decision**: FIXED — Revoking_trainer_from_a_non_active_account_is_409 added; asserts 409 not_active and that the role survives the refusal.

### F6 — Two comments describe the wrong failure mechanism

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/Application/Members/MemberAdminEndpoints.cs:64-66` and `:359`;
  `src/app/src/app/core/admin/member-admin.models.ts` (`TrainerRoleFailure`)
- **Detail**: Two claims I wrote are wrong. First, `TrainerRoleFailure`'s doc says "there is no race
  of ours to lose" — but `AddToRoleAsync` goes through `UpdateUserAsync`, which is a read-then-write
  against the `ConcurrencyStamp` token, so a concurrent `BlockAsync` (which rotates that stamp at
  `:251`) makes the role write fail with a genuine concurrency failure. The SPA's generic-409
  refetch handles it correctly, but the stated reasoning is false, and the models file compounds it
  by claiming `failed` "in practice means a concurrent change already produced the outcome asked
  for" — in the concurrent-block case it emphatically did not.

  Second, the comment at `:359` assumes the only realistic failure is a lost race. Identity's
  `UserStore.AddToRoleAsync` **throws** `InvalidOperationException` when the role row is missing
  rather than returning a failed `IdentityResult` — and `Program.cs` deliberately swallows seeder
  failures, so a started-but-unseeded app answers this endpoint with a 500.
- **Fix**: Correct both comments; optionally catch the missing-role case and return the named 409
  instead of a 500.
- **Decision**: FIXED (comments only) — both the TrainerRoleFailure doc and the models.ts mirror now describe a genuine concurrency failure; the missing-role 500 path is documented rather than caught.

### F7 — Mixing Identity's own save with the file's one-save discipline

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: `src/Application/Members/MemberAdminEndpoints.cs:357`, `:397`
- **Detail**: `AddToRoleAsync`/`RemoveFromRoleAsync` call `SaveChangesAsync` on the same scoped
  `DbContext` that `ApproveAsync` and `BlockAsync` deliberately keep under a single manual save.
  Harmless today — nothing else is tracked in these two handlers — but the moment a later edit adds
  anything before the role call (an outbox row for "you are now a trainer", say), Identity's
  internal save commits it as an invisible side effect, defeating the discipline the rest of the
  file documents at length. The plan chose this pattern deliberately and for good reasons; the gap
  is that nothing warns the next editor.
- **Fix**: Add a comment at both call sites stating that the role call saves, so nothing may be
  staged on the context before it.
- **Decision**: SKIPPED — no warning comment added at the AddToRoleAsync call sites.

### F8 — `MemberQuery`'s performance rationale is wrong

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Infrastructure/Members/MemberQuery.cs:39-43`
- **Detail**: The comment claims the correlated projection avoids "multiplying the user row once per
  role and forcing a regroup in memory". Under EF Core's default `SingleQuery` behaviour that is
  what it does anyway — one statement with a `LEFT JOIN` and a client-side regroup. There is no N+1
  (independently checked: no `UseQuerySplittingBehavior` anywhere in the repo), so the behaviour is
  correct and fine at one-gym scale; only the stated reason is wrong, and it would mislead the next
  person tuning this.
- **Fix**: Restate the comment as "EF emits a single statement under the default SingleQuery
  behaviour" and drop the fan-out claim.
- **Decision**: FIXED — comment restated: one round-trip under default SingleQuery; the fan-out avoidance claim removed.

### F9 — Optimistic patch can render a duplicate role badge

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/features/admin/members/members.ts:226-231`
- **Detail**: The grant path patches with `[...row.roles, TRAINER_ROLE]` and no dedupe. On the
  server's idempotent 200 path — the account already held the role but the loaded row was stale —
  the screen renders two "Trener" badges until the next load. Client-only and cosmetic; nothing
  persisted is affected.
- **Fix**: Guard with `includes` before appending.
- **Decision**: SKIPPED — duplicate badge on the idempotent path left as is.

### F10 — Role-name constants now exist in three places in the SPA

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/app/src/app/features/admin/members/members.ts:22-24`;
  `src/app/src/app/core/auth/auth.service.ts:28`
- **Detail**: `members.ts` declares `MEMBER_ROLE`/`ADMIN_ROLE`/`TRAINER_ROLE` as local literals,
  while `auth.service.ts` inlines `'Admin'` for its own check. Three copies of the same wire
  constant. Not wrong — the local declaration carries a comment explaining itself — but a shared
  `core/auth/roles.ts` is the obvious home, and `S-06` will need the Trainer name too.
- **Fix**: Extract to `core/auth/roles.ts` when `S-06` needs it; not worth a standalone change now.
- **Decision**: FIXED — extracted core/auth/roles.ts; members.ts and auth.service.ts both consume it.

## What passed

- **Plan Adherence** — every planned change in both phases verified MATCH against the code, with
  file:line evidence. All four "Adapted during implementation" notes accurately describe what the
  code does.
- **Scope Discipline** — no scope creep. Everything beyond the literal contract (Home/End keys,
  Polish role labels, the `failed` reason, the no-`User`-badge decision) is covered by an adaptation
  note or recorded in the plan.
- **The plan's five load-bearing claims all hold.** `MemberFacing` is element-for-element identical
  to the old `All`, so `ActiveMember` admits exactly the same accounts; the grant cannot remove any
  other role; `BlockAsync`'s guard is byte-for-byte unmodified; nothing from "What We're NOT Doing"
  leaked in.
- **Architecture** — the layering rule holds. `Domain/ApplicationRoles.cs` has zero usings;
  `Application` references Identity and `Domain` but not EF Core; EF stays in `Infrastructure`.
- **Success Criteria** — all five automated criteria independently re-run and green: `dotnet build`
  0 warnings, `dotnet test` 71/71, `npm test` 118/118, `npm run quality:check` clean.
- **No privilege escalation.** Every endpoint on the surface was checked against an admin-directed
  request: approve and unblock no-op on an already-active admin, block is guarded, and the role
  endpoints are the intended path. The `All`/`MemberFacing` split correctly keeps `Trainer` alone
  from satisfying `ActiveMember`.
