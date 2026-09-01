# Plan brief: Registration and approval (S-01)

**One line**: a member registers, signs in immediately as pending, waits on an awaiting-approval
screen, and the admin approves them — which flips their status and sends F-03's approval email.

## Why this slice is bigger than it sounds

It closes three loose ends the foundations left open:

1. **F-02 shipped a contradiction.** The PRD and the roadmap both say a pending member *can log in*.
   `AuthEndpoints.cs:64` refuses them. Three F-02 accommodations for pending members are unreachable
   until that is inverted.
2. **`ActiveMember` and `Admin` have no production consumers** — only environment-guarded test probes.
   The admin endpoints here are the first.
3. **F-03's outbox has no production caller.** `IAccountApprovedNotification` was built, deployed and
   left with an `INTEGRATION POINT FOR S-01` comment. Approve calls it.

## The twelve decisions

| # | Decision |
| --- | --- |
| D1 | Pending members get a session; content is gated by policy, not by refusing login |
| D2 | Blocked members stay refused at login — a revoked account gets no 30-day cookie |
| D3 | Register discloses `email_taken` (409), deliberately asymmetric with login's non-disclosure |
| D4 | No rate limiting or CAPTCHA — the approval gate is the mitigation, per FR-001 |
| D5 | Admin surface is the pending list plus approve; no reject, no block, no full member list |
| D6 | Approve calls `NotifyAsync` in one `SaveChangesAsync` — no explicit transaction |
| D7 | Hand-rolled SCSS tokens; Cormorant Garamond + Plus Jakarta Sans, self-hosted; warm neutral palette |
| D8 | Reactive forms — decides the idiom for the project |
| D9 | Polish copy, hardcoded, no i18n |
| D10 | Route paths stay English; only copy is Polish |
| D11 | The awaiting screen refreshes on a button, not a timer — and it calls `/api/auth/refresh`, not `/me` |
| D12 | `activeMemberGuard` and `adminGuard` sit beside an untouched `authGuard` |

## Phases

1. **API surface** — register, pending login, `/refresh`, admin pending list, idempotent approve,
   notification wiring. Rewrites one existing test, completes another, adds two test classes.
2. **Frontend foundation and member screens** — kills the 353-line scaffold, lays the token and
   typography layer, builds login/register/awaiting on reactive forms, adds both guards.
3. **Admin approvals screen, deploy, verify** — ends with a real approval email arriving in a real
   inbox, the first time F-03 delivers for a product action.

## Watch for

- **The `account_status` claim lives in the cookie, not the database.** Approving a member
  updates the row but not their cookie, which is re-minted only every 30 minutes. `/me` reads the
  database and would report Active while every `ActiveMember` endpoint still returned 403. This is
  why `POST /api/auth/refresh` exists and why the awaiting screen calls it. Caught at plan review
  (F1); the regression test is pinned to `/test/active-member`, because production has no
  `ActiveMember` endpoint until S-03.

- The `latin-ext` font subset. Without it, Polish diacritics break — and only at runtime.
- Both new guards must return `true` on the server platform, or prerendering fails the build.
- `Enqueue` does not save. The approve handler owns the unit of work.
- Approving twice must send one email, not two.
