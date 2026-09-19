# A failure is told one way, and the copied patterns are extracted once — Plan Brief

> Full plan: `context/changes/frontend-error-and-patterns/plan.md`
> Research: `context/changes/frontend-error-and-patterns/research.md`

## What & Why

The SPA tells a person that something failed in nine different ways, and none of them is
wrong on its own — what is missing is the rule that says which one a given failure gets. This
slice writes that rule, builds the one surface it was missing (a toast), gives all seventeen
failure vocabularies a compiler-checked message table, and extracts the seven families of
blocks that have been copied from screen to screen. Roadmap slice **S-19**, anchors M-6
CS-04…CS-07.

## Starting Point

Three of seventeen `*Failure` unions have an exhaustive `Record`-based message table; the
other fourteen are hand-written `switch` statements inside whichever component calls the
endpoint. The `HttpErrorResponse` unwrap is written by hand ~20 times, there is no handling of
403 / 429 / 500 / offline anywhere, and one server refusal (`time_conflict`) already produces
two different Polish sentences on one form. There is no toast, no z-index scale, and not a
single timer-driven or animated element in the whole app.

## Desired End State

A contributor adding a screen has exactly one decision to make about failure — which of four
outlets it takes — and the rule in `AGENTS.md` answers it. Every union has a table, so a new
server reason without a Polish sentence fails the test suite. A rate limit, a server fault and
an offline browser each read as themselves. `reject()`, the four-signal form block, the
generation counter, `setBusy`, the page header and the overlay chrome each exist once.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| What "one way" means | A rule with **four** outlets: field error, form banner, toast, screen state | `notFound` and `loadFailed` are correct where they are; collapsing them into a toast would leave an empty page behind a fading message. | Plan |
| Unwrap return shape | Classified: `{ kind, reason?, status }` | Without a `kind`, a 429 and a 500 stay indistinguishable from an unrecognised business refusal — the gap research found. | Plan |
| Message tables | One `Record` per union, built by a shared factory | Keeps the only mechanism in the repo that turns a server vocabulary change into a build error, while writing the guard once instead of seventeen times. | Plan |
| Field-level wording | Moves into the tables | The only way to kill the `time_conflict` double-wording at its source; `class-form.ts:274-309` already claims this rule and breaks it. | Plan |
| `notice()` split | Three tones: `success` / `info` / `error` | A recovered 409 is neither success nor failure; two tones would ship "Lista była nieaktualna" as a success. | Plan |
| Extraction scope | **All seven** duplicate families, not CS-06's four | User's call — accepts a wider slice to leave no known duplicate behind. | Plan |
| Toast implementation | Hand-rolled host + `LiveAnnouncer` from `@angular/cdk/a11y` | CDK is already a dependency; `LiveAnnouncer` solves the one genuinely hard part (announcing a disappearing message) without importing `cdk/overlay` into the eager bundle. | Plan |
| Form-state block | `createFormState()` factory, not a base class | The repo uses no component inheritance and every shared component is standalone; the roadmap left this call to the plan. | Plan / Roadmap |
| Phasing | Foundation → rule → migrate by area → non-error extractions | Each migration phase is independently revertible and green; the rule exists before the first screen applies it. | Plan |
| Enforcement | Types + a contract spec over all 17 unions + the rule in `AGENTS.md` | Documentation alone already failed once — `class-form.ts` carries the rule in a comment and breaks it two screens down. | Plan |

## Scope

**In scope:** the classified unwrap; the table factory; fourteen new message tables plus the
three existing ones migrated onto it; the toast component, service and z-index scale; the rule
written into `AGENTS.md`; migration of ~30 screens; extraction of `reject()`, the four-signal
form block, the generation counter, `setBusy`/`isBusy`, the page-header block (eight files,
not three) and the overlay chrome (plus the focus management the overlays never had); the
type-only `AccessCodeFailure` fix.

**Out of scope:** any backend change whatsoever — no route, status, `reason`, exception
middleware or `ProblemDetails` (CS-03, CS-07); any UI library; i18n; an `OnPush` sweep; the
route lazy/eager discrepancy in `app.routes.ts:7-13`; removing the deliberately-dead
`pending_approval`.

## Architecture / Approach

```
core/http/failure.ts            classifyFailure() → { kind, reason?, status }
core/http/failure-messages.ts   createFailureMessages(Record, unknown) → (reason) => string
core/http/transport-messages.ts words for auth / rateLimited / server / offline
        ↓ consumed by
core/**/x-failure.ts  × 17      one exhaustive Record per union
        ↓ routed by THE RULE (AGENTS.md) to one of
field error · form banner · toast (shared/toast) · screen state
        ↑ with form plumbing from
shared/forms/form-state.ts      createFormState() + reject()
```

The toast host mounts once in the shell beside `<main>`, above the bottom nav and the
overlays on a new z-index scale.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Foundation | `classifyFailure`, the table factory, transport words; three existing tables migrated as proof | Getting `FailureKind` wrong — everything downstream branches on it |
| 2. Rule + toolkit | The rule in `AGENTS.md`, the toast, the z-index scale, `createFormState()` | The app's first self-dismissing element: timing, motion and screen-reader announcement all new here |
| 3. Fourteen tables | Every union gets a table, field wording included; contract spec; `AccessCodeFailure` fix | Rewording for two contexts at once — a sentence must read right under a field and in a banner |
| 4. Migrate auth | login, register, forgot, reset, profile | Login's non-disclosure must survive verbatim |
| 5. Migrate admin | 11 screens + 3 overlays — the densest phase | `members.ts` hosts four unions; `classes.ts` holds the app's only optimistic write |
| 6. Migrate trainer + member | plans, plan-builder, my-plan, schedule, my-classes, dashboard | The dashboard's 204-vs-error distinction and its four independent cards |
| 7. Extractions + close-out | `setBusy` ×5, generation counter ~15, page header ×8, overlay chrome ×3 + focus management; bundle reckoning | Widest blast radius, lowest test coverage — pure refactor across screens whose specs do not assert layout |

**Prerequisites:** S-17 and S-18 closed (both are); no product slice in flight. Node 22+.
**Estimated effort:** ~7 sessions, one per phase; 5 and 6 are the heaviest by volume, 2 by
decision density.

## Open Risks & Assumptions

- **The toast becomes a tenth mechanism** if the rule is treated as documentation rather than
  as the thing being implemented. This is the slice's own recorded risk; Phase 2 writes the
  rule before the component, and Phase 3's contract spec is what keeps it honest.
- **The eager bundle.** The toast host and `cdk/a11y` land in the initial chunk against a
  550 kB warning — the one number this slice can move. Measured at the end of Phase 2 and
  again in Phase 7.
- **Phase 7 has the widest blast radius and the thinnest safety net.** No spec asserts header
  layout or overlay chrome, so its verification is mostly manual.
- **Scope was widened past CS-06** to all seven duplicate families, including the three
  overlays the charter never names. That is the "while I'm here" move the milestone warns
  about, accepted deliberately and confined to Phase 7.
- **Assumption:** rewording a field-level sentence so it also works in a banner will be
  acceptable in every case. Where it is not, that reason keeps two entries and the plan's
  single-sentence rule bends — to be recorded, not silently worked around.

## Success Criteria (Summary)

- A person sees a different, correct sentence when the server is down, when they are rate
  limited, and when the club's rules refused them — three cases that read identically today.
- No screen shows a failure in a way the rule does not sanction, and the contract spec fails
  if a future union arrives without a table.
- Nothing a member or an admin can *do* changed; `dotnet test`, `login.spec.ts` and
  `dashboard.spec.ts` all pass untouched.
