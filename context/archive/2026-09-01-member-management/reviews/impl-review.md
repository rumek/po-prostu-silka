<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Member Management (S-02)

- **Plan**: `context/changes/member-management/plan.md`
- **Scope**: Full plan — Phases 1–3 of 3 (30/30 Progress rows complete)
- **Date**: 2026-09-01
- **Verdict**: NEEDS ATTENTION → all findings resolved during triage (2026-09-01)
- **Findings**: 0 critical, 3 warnings, 0 observations
- **Commits reviewed**: `4cbb846`, `4b0c6b9`, `0f4f9ce`, `7993544`

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

**Plan Adherence** — every planned change verified against the code, clause by clause, including
the security-stamp rotation contract, the unblock non-rotation, the documented lost-race adaptation,
all four component-contract clauses, and the omit-status-when-unfiltered rule. No DRIFT, no MISSING,
no EXTRA.

**Scope Discipline** — 11 source files changed, all named in the plan. Every item on the
"What We're NOT Doing" list verified absent from the diff: no `Domain/` or `Persistence/` changes at
all (so no schema change and no migration), no pagination, no audit fields, `/admin/approvals`
untouched, no member-facing blocked screen, `AuthorizationPolicies.cs` untouched.

**Success Criteria** — all 30 rows re-verified independently during this review: `dotnet build`
clean (0 warnings, 0 errors), endpoint checks 200/401/400 as specified, `npm test` 74 passing,
`quality:check` clean, `npm run build` succeeds.

## Findings

### F1 — No request sequencing on the members list; concurrent loads can land out of order

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/app/src/app/features/admin/members/members.ts:89-98` and `:143-174`
- **Detail**: `setFilter()` calls `load()` with no cancellation or generation guard, so two rapid
  filter clicks put two `GET /api/admin/members?status=…` requests in flight and whichever
  *response arrives* last wins — not whichever was *requested* last. The chip highlight and the
  rendered rows can then disagree, with nothing visibly wrong.

  The same root cause produces a second symptom in `mutate()`: its success patch runs
  `rows.map(...)` against whatever `rows` holds when the mutation resolves. If a filter switch
  completed while the mutation was in flight, the member may be absent from the new list and the
  patch silently no-ops — a block that actually succeeded server-side looks like it did nothing,
  with no error shown.

  Both are narrow in practice (four chips, a small list, same-origin latency), and neither loses
  data — the server state is always correct. But the failure mode is a UI that quietly disagrees
  with the database, which is the class of thing this screen's own error handling was written to
  avoid.
- **Fix A ⭐ Recommended**: Guard `load()` with a monotonic request token — increment a counter on
  entry, capture it, and discard the response if the counter has moved on. Have `mutate()` capture
  the same token and skip its local patch (falling back to a refetch) if it changed.
  - Strength: Fixes both symptoms at the one shared root cause; ~10 lines; no change to the
    server/client split the plan deliberately chose; testable with the existing
    `HttpTestingController` harness.
  - Tradeoff: Adds a piece of bookkeeping state to a component that is currently very plain.
  - Confidence: HIGH — standard pattern, and the existing specs make it straightforward to cover.
  - Blind spot: Not verified whether Angular's `HttpClient` cancels the underlying request on
    unsubscribe here; the token guard makes that moot either way.
- **Fix B**: Disable the filter chips while `busy()` is non-empty, and refetch after every mutation
  instead of patching locally.
  - Strength: No new state; makes the race structurally impossible rather than detected.
  - Tradeoff: Costs a round-trip per mutation and makes the UI feel less responsive — it gives up
    exactly the in-place update the plan chose deliberately.
  - Confidence: MEDIUM — simpler, but trades away a decided design property.
  - Blind spot: Rapid filter switching with no mutation in flight would still race.
- **Decision**: FIXED via Fix A — generation token added to `load()` and `mutate()`; two regression tests added and verified to FAIL against the unfixed code before being accepted.

### F2 — Admin anti-join matches `role.Name`, not `NormalizedName`

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Infrastructure/Members/MemberQuery.cs:31`
- **Detail**: The exclusion filters on `role.Name == ApplicationRoles.Admin`, while Identity's own
  `UserManager.IsInRoleAsync` — used by the block endpoint's guard — compares against
  `NormalizedName`. The two agree today only because `AdminSeeder.cs:27` is the sole role-creation
  path (`roleManager.CreateAsync(new IdentityRole(role))` sets both fields together) and nothing can
  rename a role.

  Worth being precise about the blast radius: if these ever diverged, the consequence is an admin
  appearing in the member list with a Block button, **not** a blockable admin — `BlockAsync`'s
  independent `IsInRoleAsync` check would still refuse with 409 `is_admin`. So this is a
  defence-in-depth and consistency issue, not a live security hole. The two-layer guard chosen
  during planning is what contains it.
- **Fix**: Filter on `role.NormalizedName == ApplicationRoles.Admin.ToUpperInvariant()` so the query
  and `IsInRoleAsync` use the same comparison convention.
- **Decision**: FIXED — now matches on `role.NormalizedName`, aligning the query with `IsInRoleAsync`'s convention.

### F3 — Three comments still assert the security-stamp interval is 30 minutes

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Infrastructure/Identity/AppUserClaimsPrincipalFactory.cs:14`,
  `src/Infrastructure/Authorization/AuthorizationPolicies.cs:16`,
  `src/Application/Auth/AuthEndpoints.cs:220`
- **Detail**: Phase 2 changed `ValidationInterval` to 2 minutes and updated the comment at the
  change site (`Program.cs:118-130`) plus the one in `MemberAdminEndpoints.cs`. Three other comments
  elsewhere still state 30 minutes as fact, one of them citing `Program.cs` as its source:

  - `AppUserClaimsPrincipalFactory.cs:14` — *"That refresh interval - 30 minutes, set in Program.cs -
    is what…"*
  - `AuthorizationPolicies.cs:16` — *"staleness is bounded by the 30-minute security-stamp
    validation interval"*
  - `AuthEndpoints.cs:220` — *"security-stamp validator refreshes - every 30 minutes (Program.cs)"*

  This is precisely the failure mode `context/foundation/lessons.md` already records as a project
  rule: an implementation adapts correctly, but prose is left asserting the old contract, and the
  cost lands on the next reader and every future review. The lesson's stated remedy is to fix the
  assertion in the same phase that invalidates it. Three files now describe behaviour the code no
  longer has, and each is exactly the kind of comment a future reader would trust over the code.
- **Fix**: Update all three to reference the interval without hardcoding a stale number — cite
  `Program.cs` as the single source of truth (e.g. "the security-stamp validation interval set in
  Program.cs") so a future change to the value cannot strand them again.
- **Decision**: FIXED — all four stale assertions (AuthEndpoints.cs carried two) now cite `Program.cs` as the single source of truth instead of restating the number.
