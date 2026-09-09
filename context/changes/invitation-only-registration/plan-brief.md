# Invitation-only registration — Plan Brief

> Full plan: `context/changes/invitation-only-registration/plan.md`

## What & Why

Registration stops being a public door. The club's desk enters a person into the records — with or
without an email address — and the only way to attach a login to those records is an invitation code
handed over in person. What registration asks for shrinks to the two things the club cannot know for
someone: an address to sign in with, and a password.

## Starting Point

`POST /api/auth/register` takes eight fields plus an **optional** member code: with a code it claims
the member record the club already keeps, without one it creates a fresh record. `/register` carries
no route guard. Nothing in the SPA links to it — half of the "no public door" goal is already true by
accident rather than by design.

## Desired End State

`/register` with no query lands on `/login`; with a live session it lands on `/`. With
`?invitationCode=ABCD-2345` it shows a form carrying the code, filled in and not editable, asking
only for an email address and a password. Submitting it attaches the account to that member's record,
and the person arrives on the dashboard with their bookings, karnet and plan already there. The API
refuses any registration with no code.

## Key Decisions Made

| Decision                        | Choice                                        | Why (1 sentence)                                                                                    |
| ------------------------------- | --------------------------------------------- | --------------------------------------------------------------------------------------------------- |
| Query parameter vs API field    | `invitationCode` in the URL, `memberCode` on the wire | The friendlier word reaches the person; no shipped contract has to move.                       |
| No code on `/register`          | Redirect to `/login`                          | Silence discloses nothing about whether a code exists.                                              |
| Refused code (expired, revoked) | Redirect to `/login` with a message           | The field is readonly, so there is nothing on that screen the member could fix.                     |
| Already signed in               | Redirect to `/`                               | Stops a second account being created on a shared device by accident.                                |
| Contract shape                  | Delete the six dropped fields; `MemberCode` required | The only client is this repo's own SPA, and a contract that lies rots for years.              |
| Missing phone / address         | Leave it to the existing `/profile` prompt    | `profile.html:7` already asks; a dashboard nudge would add a second voice for the same thing.        |
| Guessing the code               | Keep the existing per-IP rate limit           | ~8.5 × 10¹¹ combinations against a handful of live codes is already uneconomic to scan.             |
| Admin surface                   | Add "copy invitation link" beside the code    | Someone has to produce the URL, and hand-assembling it per member invites typos.                     |

## Scope

**In scope:** the API refusal and the shortened contract; a route guard on `/register`; the
prefilled readonly code and the two-field form; the login screen's "invitation no longer valid"
message; a copy-invitation-link action in the member panel; the stale copy S-16 left behind on the
register screen.

**Out of scope:** generating, expiring and revoking codes (shipped in S-14); the code's format,
alphabet and 14-day validity; `PUT /api/profile`, where the contact fields stay required; any schema
migration; extra brute-force hardening.

## Architecture / Approach

Back to front, refusal first: the API stops accepting codeless registrations before the SPA stops
sending them, so no screen ever promises what the server has withdrawn. The SPA's guard and form land
together — a new door in front of the old eight-field form would be half a slice. The claim path
itself barely changes: it already drops the submitted display name in favour of the club's, and once
the contact fields stop arriving, deleting their overwrite is the whole edit.

## Phases at a Glance

| Phase                                    | What it delivers                                              | Key risk                                                                                  |
| ---------------------------------------- | ------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| 1. The API refuses a codeless registration | Three-field contract, claim-only handler, inverted tests      | Removing required inputs from a write path that has had them since S-01                    |
| 2. The register screen                   | Guard, prefilled readonly code, two fields, refusal redirect  | The redirect rule must not swallow `email_taken`, which the member can still fix           |
| 3. The invitation link and the documents | Copy-link action in the panel; charter records the extra scope | Touches a surface the charter had declared out of scope — recorded rather than done quietly |

**Prerequisites:** S-14 (the member code and the claim path) and S-16 (registration that produces an
active account); both archived.
**Estimated effort:** ~3 sessions, one per phase.

## Open Risks & Assumptions

- Every member record used as an invitation target must have a display name, since registration no
  longer supplies one. The admin form requires it (`member-form.ts:56`) — the assumption is that the
  API validates it too, which Phase 1 confirms rather than assumes.
- A refused code discards the typed email and password when it redirects. Accepted deliberately: the
  member cannot fix a readonly code, so keeping them on a dead form would be the worse trade.
- The prerender must not bake a redirect into `/register`'s static HTML; the guard returns `true` on
  the server, following `active-member.guard.ts:26`.

## Success Criteria (Summary)

- A person with an invitation link reaches a form with their code already in it, supplies an email
  and a password, and lands on the club's existing record for them.
- A person without one cannot reach the form, and cannot register by calling the API directly.
- An invitation that has been used, revoked or has expired says so on a screen the member can act on.
