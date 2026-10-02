<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Expiring Karnets Dashboard Card

- **Plan**: context/changes/expiring-passes-dashboard/plan.md
- **Scope**: Phases 1–4 of 4 (full plan)
- **Date**: 2026-10-02
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Notes:
- **Automated verification, re-run during this review:**
  - `dotnet build`: 0 warnings, 0 errors.
  - `dotnet test`: 803 passed.
  - `ng test`: 962 passed.
  - `quality:check`: clean.
  - `ng build`: initial total 584.99 kB.
- **Phase 4:** the full E2E suite (13/13) and the deliberate break were recorded during implementation.
- **Manual rows 1.3, 2.4, 2.5, 3.5–3.7 and 4.3 are pending**, awaiting the user. None is checked off without evidence.
- **Architecture:** EF Core stays in Infrastructure (`ExpiringPassPredicate`, `MemberQuery`). Application holds only records, the interface and the handler.
- **Scope:** every "What We're NOT Doing" item is respected. There is no row marker, no re-sort, no trainer surface and no schema change.

## Findings

### F1 — Test inserts a class at now+2h into the shared test database

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: tests/po-prostu-silka.Tests/MemberEndpointTests.cs:863
- **Detail**: `SpendAnEntryAsync` writes a class directly at `DateTimeOffset.UtcNow.AddHours(2)` into the
  container that every `IntegrationCollection` suite shares. The no-overlap rule is club-wide, and
  `ClassEndpointTests` / `BookingEndpointTests` keep their own far-future slot bases (2030, 2032, …)
  precisely so that one file's classes never refuse another's with `time_conflict`. A near-now class
  bypasses that convention. Any API-created class that lands within that hour, now or in a future
  test, would fail depending on execution order. Entry consumption (`EntryConsumption.ConsumesAnEntry`)
  does not depend on the class's date, so the near-now start buys nothing.
- **Fix**: Start the class at a fixed far-future base of its own (e.g. 2045, plus a per-call
  `Interlocked.Increment` day offset), following the existing slot-allocator convention.
- **Decision**: FIXED — class starts at a 2045 slot base (NextSpendSlot); MemberEndpointTests 76/76 green

### F2 — Plan names a projection helper that the implementation inlined

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: context/changes/expiring-passes-dashboard/plan.md (Phase 1, §1 Contract) vs src/Infrastructure/Members/MemberQuery.cs:193
- **Detail**: The plan's contract for `ExpiringPassPredicate` promises "an expression or helper the
  card uses to project that current karnet's `ValidTo`". It was dropped, because EF cannot compose an
  `Expression<Func<Member, DateOnly?>>` inside an anonymous projection without an expander. The card
  query writes the "covering today, first by `ValidFrom`" subquery inline instead, which is now its
  third copy in `MemberQuery`. The behaviour is correct and tested. Only the plan states something
  untrue, which is exactly what lessons.md's "Record necessary adaptations in the plan" rule exists for.
- **Fix**: Add an "**Adapted during implementation.**" note to Phase 1 §1's Contract recording the inline projection and why.
- **Decision**: FIXED — adaptation note added to Phase 1 §1 Contract
