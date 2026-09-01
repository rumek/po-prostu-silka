# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Record necessary adaptations in the plan, not only in the deploy log

- **Context**: `context/changes/notification-delivery-foundation/plan.md:580-581` vs
  `src/app/angular.json` (F-03 Phase 4). Same pattern in
  `context/archive/2026-08-31-auth-identity-foundation/plan.md:362` vs
  `src/Application/Auth/AuthEndpoints.cs:56` (F-02 Phase 2).

- **Problem**: When an API turns out to differ from what the plan assumed, the implementation adapts
  correctly but the plan text is left stating the original, wrong contract. F-03's plan still
  specifies `"serviceWorker": true` + `"ngswConfigPath"` — syntax that fails schema validation on
  Angular 22's builder — with the real adaptation recorded only in `deploy-plan.md`'s gotchas. F-02's
  plan still specifies `PasswordSignInAsync`, where the code correctly uses
  `CheckPasswordSignInAsync` because the planned call would issue a cookie before the status check
  could refuse it. Both adaptations were right; both left the plan asserting something untrue. The
  cost lands on the next reader — and on every future review, which re-flags the same non-issue.

- **Rule**: When an implementation deviates from a plan contract, add an "**Adapted during
  implementation.**" note to that contract in `plan.md` *as part of the same phase*, before the
  phase-end commit. Recording it in `deploy-plan.md`, a commit message, or the review report is not a
  substitute — those are logs, and the plan is what the next reader and the next review treat as
  ground truth.

- **Applies to**: Every `/10x-implement` phase. Strongest when the deviation was *necessary* rather
  than optional — that is exactly the case where a future reader would otherwise assume the plan was
  simply not followed.
