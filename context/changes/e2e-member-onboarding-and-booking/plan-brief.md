# E2E: Invitation Claim and Staff Booking — Plan Brief

> Full plan: `context/changes/e2e-member-onboarding-and-booking/plan.md`

## What & Why

Three Playwright specs protect the journeys no current test sees end to end: an invited person claiming
the club's record with its karnet and booking (`REG-01`), a trainer's booking reaching the member's Start
(`BOOK-01`), and a booking without a valid karnet being refused with the right sentence (`BOOK-03`). They are
the first multi-persona specs, so the data-setup pattern they introduce is the one S-32 and every later spec
copies.

## Starting Point

Four admin-only specs exist. The only one that arranges data does it inline, books a fixed "an hour from now"
slot that collides club-wide, and needs a trainer that a fresh database does not have. Registration is capped
at 3 per 5 minutes per client, and members and accounts cannot be deleted.

## Desired End State

`npm run e2e` is green on the dev database, on an admin-only database, and twice in a row. Each new spec has
been shown to fail when its behaviour breaks. After a run, only `E2E …`-named members and accounts and the one
E2E trainer remain.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Registration rate limit | Not tested. Stepped around with a unique `X-Forwarded-For` per registration | An edge case that integration owns; this avoids 429s without touching production code. |
| Leftover data | Members and accounts stay, named `E2E … <ts>` with `@example.test` addresses; everything removable is removed | No delete capability exists, and S-29 owns erasure. |
| Trainer | One fixed E2E trainer, get-or-created by `trainer.setup.ts` once per run | Works on a fresh CI database without adding a trainer per run or racing workers. |
| Spec split | Three files, one test each | Follows `e2e/CLAUDE.md`, and a failure names one risk. |
| Arrange vs act | Arrange through the admin API, drive the UI only where the risk lives | Fast and stable; the forms have their own coverage. |
| Booking actor | The E2E trainer, on a class they instruct | Exercises the trainer's schedule and name-only candidate search end to end. |
| Class slot | Random day +1…+6, random 15-minute start, retry on `time_conflict` | The overlap rule is club-wide and parallel runs share one database. |
| Invitation link | Read from the clipboard after "Kopiuj link", opened in an anonymous context | The copied link itself is under test, not a URL the spec rebuilds. |

## Scope

**In scope:** the `e2e/support/` layer (fixtures, builders with cleanup, slot helper, sessions);
`trainer.setup.ts`; three specs; moving `back-closes-open-overlay` onto the helpers; `e2e/CLAUDE.md` and
`test-plan.md` §4/§5/§6.6/§6.7.

**Out of scope:** CI wiring (S-30), other `BOOK-03` refusals, trainer narrowing (`MayActOn`), the admin
list's "Bez konta" change, UI-driven member and karnet forms, any production change.

## Architecture / Approach

The specs import `test` from `e2e/support/fixtures`, which supplies an admin `APIRequestContext` and a cleanup
registry. The registry is torn down in reverse creation order, which gives booking → class → karnet → type.
Builders call the admin API and register their own cleanup. Extra personas get their own browser contexts:
anonymous (with a unique XFF) or signed in through `/api/auth/login`. They never log in through the form.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Shared E2E support | Builders, cleanup, slots, sessions, E2E trainer; overlay spec migrated | The cleanup order and the idempotent trainer are what break on the second run |
| 2. Invitation claim spec | REG-01 end to end, break-verified | Clipboard permissions and the anonymous context's headers |
| 3. Trainer booking + refusal specs | BOOK-01 and BOOK-03 end to end; test-plan updated | Bringing the class's week into view in the trainer calendar |

**Prerequisites:** Docker SQL Server up, migrations applied, Node 22+. S-17 and S-25 are done.
**Estimated effort:** about 2 sessions across 3 phases.

## Open Risks & Assumptions

- The XFF workaround relies on how the limiter picks its partition. If that changes, specs get a loud 429,
  not a silent pass.
- Leftover `E2E` rows grow the dev and staging member lists. That is accepted, and `TestDataSeed:Reset`
  clears them locally.
- Chromium clipboard permissions on `localhost` are assumed to work headless. If not, the fallback is reading
  the code shown on screen, which is a weaker assertion that would be recorded in the plan.

## Success Criteria (Summary)

- A broken invitation link, claim or Start card turns the suite red.
- A trainer's booking that does not reach the member, or a refusal shown in the wrong words, turns it red.
- The suite reruns green immediately and on an empty database.
