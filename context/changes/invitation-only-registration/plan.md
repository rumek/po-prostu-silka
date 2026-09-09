# Invitation-only registration — Implementation Plan

## Overview

Registration stops being a public door. `/register` is reachable only with an `invitationCode` query
parameter, the code arrives prefilled and readonly, the form asks for an email address and a password
and nothing else, and the API refuses any registration that carries no code. Everything the form
stops asking for — display name, phone, postal address — already lives on the member record the code
attaches to.

This is roadmap S-17, M-5's only slice. Scope anchors `IR-01`–`IR-05` live in
`context/foundation/roadmap.md`.

## Current State Analysis

`RegisterAsync` (`src/Application/Auth/AuthEndpoints.cs:259`) validates in a deliberate order —
display name, email, password, `ContactDetails.TryCreate`, then the code — so that a malformed
submission creates nothing. The member code is **optional** (`RegisterRequest.MemberCode = null`,
`AuthEndpoints.cs:31`): with a code the new account claims an existing `Member` row
(`AuthEndpoints.cs:415`), without one it creates a fresh `Member` (`AuthEndpoints.cs:449`).

The SPA's register screen (`src/app/src/app/features/auth/register/register.ts:34`) posts eight
fields plus an optional code, and `/register` carries no route guard
(`src/app/src/app/app.routes.ts:27`).

Four facts shape this plan:

1. **Nothing in the SPA links to `/register` already.** A search across `features/` and `core/`
   returns only the route definition and `auth.service.register()`. Half of IR-02 is already true;
   this slice adds the guard, not the delinking.
2. **The claim path already drops the submitted display name** (`AuthEndpoints.cs:427`) — "the club
   keeps the name it gave them", the same rule as S-13 FR-006. It does, however, *overwrite* phone
   and address from the submission (`AuthEndpoints.cs:434`). Once those fields stop arriving, the
   club's own copy is all there is, which is the correct outcome and needs no extra logic — only the
   deletion of the overwrite.
3. **`ContactDetails` is shared with `PUT /api/profile`** (`src/Application/Members/ContactDetails.cs`),
   where the five fields stay required. Registration must stop *calling* it; the type itself and the
   profile contract are untouched.
4. **`CurrentUser` reads contact details from the `Member`, not the account**
   (`AuthEndpoints.cs:838`). An invited member therefore signs in with whatever the admin recorded,
   and `profile.html:7` already prompts for what is missing.

## Desired End State

Opening `/register` with no query parameter lands on `/login`. Opening it while signed in lands on
`/`. Opening it with `?invitationCode=ABCD-2345` shows a form with the code filled in and not
editable, asking only for an email address and a password. Submitting it attaches a new account to
the member record that code belongs to, and the person arrives on the dashboard with their bookings,
their karnet and their plan already there. `POST /api/auth/register` without a member code answers
400 and creates nothing.

Verify by: `dotnet test` from the repo root, `npm test` and `npm run quality:check` from `src/app/`,
and the manual steps in `## Testing Strategy`.

### Key Discoveries

- `MemberAccessCode` (`src/Application/Members/MemberAccessCode.cs`) is 8 characters over a
  31-symbol alphabet with the confusable pairs removed, stored without a separator and displayed
  with one (`ABCD-2345`). `TryNormalise` already accepts lowercase, the display dash and stray
  spaces — a code pasted from a URL needs no new parsing.
- The code's own doc comment claims "`/register` carries no rate limit". That has been false since
  S-16 wired `RateLimitPolicies.Register` (`AuthEndpoints.cs:147`). The claim is load-bearing for
  this slice's threat model, so it gets corrected rather than left to mislead.
- Guards in this codebase return `true` on the server (`active-member.guard.ts:26`) because every
  route is prerendered and a guard that decided anything would bake a redirect into static HTML.
  The new guard must do the same.
- `register.html:4` still tells the visitor their account awaits staff approval — stale since S-16
  retired approval.

## What We're NOT Doing

- **Not issuing invitations.** Generating, showing, expiring and revoking a member code shipped with
  S-14 and is untouched. The one addition is a way to copy the code as a URL (Phase 3).
- **Not renaming the contract.** The query parameter is `invitationCode`; the API field stays
  `memberCode`. Settled with the user; recorded in the M-5 charter.
- **Not changing the code format, alphabet, length or 14-day validity.**
- **Not touching `PUT /api/profile`.** The five contact fields stay required there.
- **Not adding a dashboard nudge** for members with no phone or address. `profile.html:7` already
  prompts; that is the whole answer.
- **Not adding a schema migration.** No column changes.
- **Not hardening the code against guessing beyond the existing rate limit.** ~8.5 × 10¹¹
  combinations against a handful of live codes, behind a per-IP limiter, was judged sufficient.

## Implementation Approach

Back to front, refusal first. The API stops accepting codeless registrations before the SPA stops
sending them, so at no point does a screen promise something the server has already withdrawn — and
if the slice were abandoned halfway, the door would be shut rather than propped open by a form the
server no longer serves.

Within the SPA the guard and the form land together (Phase 2): they are one screen's contract, and
a guard without a shortened form leaves the old eight fields behind a new door.

## Critical Implementation Details

**Only a rejected CODE redirects.** The user chose `/login` with a message for a code the API
refuses, because the readonly field leaves the member nothing to fix. That rule must not spread to
`email_taken`, which stays on the email control with its "Zaloguj się" link — that failure *is*
fixable, and redirecting it would throw away a typed password for no reason. The two failures share
an endpoint and must not share a handler.

**The guard runs on the server too.** Return `true` when `isPlatformServer`, exactly as
`active-member.guard.ts:26` does, or the prerender bakes a redirect into `/register`'s static HTML.

---

## Phase 1: The API refuses a registration with no code

### Overview

`POST /api/auth/register` becomes claim-only: a code is required, the contact fields are gone, and
the branch that created a fresh member record is deleted.

### Changes Required:

#### 1. The request contract

**File**: `src/Application/Auth/AuthEndpoints.cs`

**Intent**: `RegisterRequest` loses `DisplayName`, `PhoneNumber`, `Street`, `HouseNumber`,
`PostalCode` and `City`, and `MemberCode` stops being optional. The record's doc comment explains
that registration is now claim-only and where the dropped fields come from instead.

**Contract**: `record RegisterRequest(string Email, string Password, string MemberCode)` — three
required fields, no defaulted parameter.

#### 2. The handler

**File**: `src/Application/Auth/AuthEndpoints.cs`

**Intent**: Drop the display-name and `ContactDetails.TryCreate` validation blocks; require the code
where they used to sit, so a codeless request still costs one round trip and creates nothing. The
account's `DisplayName` and `PhoneNumber` now come from the claimed member rather than the request.
Delete the `else` branch that created a fresh `Member`, and delete the contact-detail overwrite in
the claim branch — the club's record is the only source now.

**Contract**: A missing, blank or malformed code answers `400 invalid_member_code`; an unknown,
expired, revoked, already-claimed or blocked-member code keeps answering `409 unknown_member_code`,
collapsed into one answer for the account-enumeration reason already recorded at
`AuthEndpoints.cs:314`. `email_taken` (409) is unchanged. `claimed.Email ??= request.Email` stays —
it is what gives a record entered without an address the login address as its contact.

#### 3. The stale rate-limit claim

**File**: `src/Application/Members/MemberAccessCode.cs`

**Intent**: Correct the `Alphabet` doc comment, which argues the code space is large enough "even
though `/register` carries no rate limit". It has carried one since S-16, and that limiter is now
part of why no further hardening was added.

**Contract**: Prose only.

#### 4. Backend tests

**File**: `tests/po-prostu-silka.Tests/MemberClaimTests.cs`

**Intent**: Invert `Registering_without_a_code_still_creates_a_fresh_record` into a refusal, and add
coverage for the shortened contract: a claim carries no contact fields and leaves the member's own
phone and address intact; a member record with no email gets the login address written into it.
Every existing code-failure test (single use, revoked, expired, blocked, malformed, lowercase, the
two-people race) keeps passing unchanged — they are the regression net for the refusal ordering.

**Contract**: `Registering_without_a_code_is_refused` asserts 400 `invalid_member_code` and that no
`ApplicationUser` and no `Member` row were created.

**Adapted during implementation.** This contract named only `MemberClaimTests.cs`, but
`tests/po-prostu-silka.Tests/RegisterEndpointTests.cs` — 16 tests the plan never mentions — is built
entirely on codeless registration and the six fields being removed, so it could not survive the
contract change untouched. It was rewritten in this phase: every surviving test now arranges an
invited member and carries a live code, and seven tests lost their subject and were deleted
(`Blank_display_name_is_rejected`, `Display_name_is_trimmed_before_it_is_stored`,
`Contact_details_are_stored_with_the_phone_number_normalised`, `Malformed_postal_code_is_rejected`,
`Malformed_phone_number_is_rejected`, `Blank_city_is_rejected`,
`A_registration_refused_for_contact_details_creates_no_account`). No coverage was lost: the five
contact-field failure codes are still exercised against `PUT /api/profile` in `ProfileEndpointTests`
and against the admin surface in `MemberEndpointTests`, which is where those fields still live.

Three tests were added rather than merely moved, all pinning behaviour the shortened contract makes
newly load-bearing: `Duplicate_email_is_disclosed_as_email_taken_without_consuming_the_code` and
`Malformed_email_is_rejected_without_echoing_identity_error_text` assert the invitation SURVIVES a
refused attempt — with the code now the only way in, burning it on a typo would be unrecoverable —
and `A_records_own_address_is_not_a_duplicate_when_that_record_is_the_one_claimed` covers the
`exceptMemberId` path that a member claiming the record already holding their address depends on.

**Also settled during implementation.** The roadmap's stated risk — "a member record with an empty
display name would produce a nameless account" — needs no new guard: `MemberAdminEndpoints.cs:850`
already refuses a blank display name on both create and edit, so every member record has one before
a code can be issued against it.

### Success Criteria:

#### Automated Verification:

- Backend builds warning-free: `dotnet build` from `src/`
- Integration tests pass: `dotnet test` from the repo root
- No `ContactDetails` reference remains in `RegisterAsync`: `grep -n "ContactDetails" src/Application/Auth/AuthEndpoints.cs` returns only the `RegisterFailure` doc comment

#### Manual Verification:

- `POST /api/auth/register` with no `memberCode` answers 400 and creates nothing (checked against the DB)
- A valid code still claims: the new account signs in and its dashboard shows the record's existing bookings and karnet

---

## Phase 2: The register screen — a guarded door and two fields

### Overview

`/register` gets a guard and a form that asks for an email address and a password beside a readonly
code.

### Changes Required:

#### 1. The guard

**File**: `src/app/src/app/core/auth/invitation.guard.ts` (new)

**Intent**: Refuse `/register` to anyone arriving without an invitation, and to anyone already
signed in. Follows `active-member.guard.ts`'s shape, including the server-side bypass.

**Contract**: `CanActivateFn`. Returns `true` on the server. In the browser: a live session →
`router.createUrlTree(['/'])`; a missing or blank `invitationCode` query parameter →
`router.createUrlTree(['/login'])`; otherwise `true`. The code's *validity* is never checked here —
that answer belongs to the API, and asking for it before the form would hand an anonymous caller a
code oracle.

#### 2. The route

**File**: `src/app/src/app/app.routes.ts`

**Intent**: Attach the guard to `register` and record why the route is no longer guard-free — the
comment two lines below it still groups register with login as anonymous-by-design.

**Contract**: `{ path: 'register', component: Register, canActivate: [invitationGuard] }`.

#### 3. The form

**File**: `src/app/src/app/features/auth/register/register.ts`

**Intent**: Reduce the form to `email`, `password` and `memberCode`. The code is seeded from the
`invitationCode` query parameter and disabled from editing; the six dropped controls and their
failure mappings go with the fields. A code the API refuses navigates to `/login` carrying a reason;
`email_taken` and `invalid_password` stay on their controls.

**Contract**: `memberCode` is a readonly control whose value comes from
`ActivatedRoute.snapshot.queryParamMap.get('invitationCode')`. On `invalid_member_code` or
`unknown_member_code`, navigate to `/login` with a query parameter the login screen renders as a
message. Use a readonly input rather than a disabled one — a disabled control is dropped from
`getRawValue()`'s payload in the template-bound path and would post an empty code.

#### 4. The template

**File**: `src/app/src/app/features/auth/register/register.html`

**Intent**: Delete the six field blocks and the stale approval notice at the top; the code field
becomes readonly and loses its "(opcjonalnie)" label and its "leave it blank" hint. The page explains
in one line that this account will attach to the record the club already keeps.

**Contract**: Three fields in DOM order: email, password, code (readonly). The `unknown` /
`pattern` error branches on the code control are removed — those failures now leave the screen.

#### 5. The login screen's message

**File**: `src/app/src/app/features/auth/login/login.ts` and `login.html`

**Intent**: Render a message when the register screen bounced someone here with a refused
invitation, so the redirect is not silent.

**Contract**: A query parameter read on init (`?reason=invalid-invitation`), shown as an alert:
the invitation is no longer valid and the club can issue a new one. Absent parameter → nothing
rendered.

#### 6. The request model

**File**: `src/app/src/app/core/auth/auth.models.ts`

**Intent**: Mirror the API's new three-field `RegisterRequest`.

**Contract**: `{ email: string; password: string; memberCode: string }` — `memberCode` no longer
optional or nullable.

#### 7. Frontend tests

**File**: `src/app/src/app/features/auth/register/register.spec.ts`, plus a new
`src/app/src/app/core/auth/invitation.guard.spec.ts`

**Intent**: Cover the door and the shortened form. The register spec loses its assertions about the
six dropped fields and gains: the code is prefilled from the query and cannot be edited, the payload
carries exactly three fields, and a refused code navigates to `/login`. The guard spec covers all
three of its answers plus the server bypass.

**Contract**: Existing Vitest + `provideHttpClientTesting` setup; no new test infrastructure.

**Adapted during implementation.** Three departures from this phase's contracts, all necessary:

1. **`login.spec.ts` was touched too**, which contract #7 does not name. Contract #5 adds rendered
   behaviour to the login screen, and leaving it uncovered would have shipped the one half of the
   redirect the register spec cannot see. Two tests: the message renders for
   `?reason=invalid-invitation`, and an ordinary visit renders no alert.
2. **`ProfileRequest` in `auth.models.ts` lost a stray `memberCode` field.** It was a copy-paste of
   `RegisterRequest`'s doc comment and field into the profile mirror type; `ProfileRequest` on the
   server (`src/Application/Members/ProfileEndpoints.cs:19`) has five fields and never had a sixth,
   and nothing in the SPA ever set it. Removing it corrects a mirror type that was lying, and touches
   no behaviour — `PUT /api/profile` is unchanged, as "What We're NOT Doing" requires.
3. **`RegisterFailureReason` was narrowed**, which contract #6 does not mention: `invalid_display_name`
   and the five `ContactFailureReason` codes left the union along with the fields that produced them.
   The union is a mirror of what the endpoint can answer, and the endpoint can no longer answer with
   any of them.

The two code failures were also collapsed into ONE handler rather than two — `invalid_member_code`
and `unknown_member_code` both navigate to `/login?reason=invalid-invitation`, because the member's
next step is identical either way and the readonly field leaves nothing to distinguish for them. The
contract asked for the same destination for both; this only says it once.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test` from `src/app/`
- Prettier + ESLint clean: `npm run quality:check` from `src/app/`
- Build succeeds inside the 550 kB initial-bundle budget: `npm run build` from `src/app/`

#### Manual Verification:

- `/register` with no query redirects to `/login`
- `/register?invitationCode=…` while signed in redirects to `/`
- The code field shows the code from the URL and cannot be typed into or pasted over
- Registering with a valid code lands on the dashboard with the record's bookings and karnet present
- A revoked or expired code sends the visitor to `/login` with a readable message
- An email address already in use stays on the form with its error, and the typed password survives

---

## Phase 3: The invitation link, and the documents

### Overview

The admin panel gains a way to copy the whole invitation URL, and the charter records that this
extended M-5's stated scope.

### Changes Required:

#### 1. The copy-link action

**File**: `src/app/src/app/features/admin/members/members.ts` and `members.html`

**Intent**: Beside the existing "Pokaż kod klubowicza" reveal, add an action that copies the full
`/register?invitationCode=…` URL. The bare code stays visible — it exists to be read down the phone,
which is what its alphabet was designed for.

**Contract**: The URL is built from the document's own origin plus the displayed code, and reuses
the clipboard path already behind the code copy, including its "Nie udało się skopiować" fallback
(`members.html:242`).

#### 2. The charter

**File**: `context/foundation/roadmap.md`

**Intent**: M-5's charter says issuing the invitation is out of scope. Copying the link is part of
issuing it, and the user extended the scope deliberately during planning — the document should say
so rather than let the code and the charter disagree.

**Contract**: An `IR-06` anchor for the copy-link action, and the "Not in scope" line narrowed to
generating, expiring and revoking the code.

#### 3. Frontend tests

**File**: `src/app/src/app/features/admin/members/members.spec.ts`

**Intent**: Cover that the copied URL carries the member's code and the `invitationCode` parameter
name.

**Contract**: Existing spec setup; the clipboard is already stubbed there for the code copy.

**Adapted during implementation.** The clipboard was NOT already stubbed in `members.spec.ts` — no
test there had exercised `copyCode()`. The new test stubs it itself, through
`Object.defineProperty(navigator, 'clipboard', …)` rather than `vi.stubGlobal('navigator', …)`:
replacing the whole navigator drops the prototype getters Angular's `DefaultValueAccessor` reads
(`userAgent`), which fails every render in the file rather than the one test. It also asserts the
bare-code copy still works, since the panel now has two copy actions where it had one.

### Success Criteria:

#### Automated Verification:

- Frontend unit tests pass: `npm test` from `src/app/`
- Prettier + ESLint clean: `npm run quality:check` from `src/app/`
- Full backend suite still green: `dotnet test` from the repo root

#### Manual Verification:

- The admin copies an invitation link, opens it in a private window, and reaches a register form with
  the code filled in
- The bare code is still readable in the panel for reading aloud

---

## Testing Strategy

### Unit Tests

- `invitation.guard.spec.ts`: no query → `/login`; live session → `/`; valid query → allowed; server
  platform → allowed without touching the router.
- `register.spec.ts`: the code prefills from the query and is readonly; the payload has exactly
  `email`, `password`, `memberCode`; `unknown_member_code` navigates to `/login`; `email_taken` does
  not navigate.
- `members.spec.ts`: the copied invitation URL contains `?invitationCode=` and the member's code.

### Integration Tests

- `MemberClaimTests`: registration with no code is refused and writes nothing; a claim with the
  three-field payload attaches the account and leaves the member's phone and address untouched; a
  member with no email gets the login address written in; every existing code-failure case still
  answers as before.

### Manual Testing Steps

1. Admin creates a member with a display name and no email address, and issues a code.
2. Admin copies the invitation link and opens it in a private window — the code is filled in and not
   editable.
3. Register with an email and password; land on the dashboard; confirm the display name is the club's
   and the karnet and bookings are present.
4. Reuse the same link — expect `/login` with the "invitation no longer valid" message.
5. Open `/register` with no query — expect `/login`.
6. Open the invitation link while signed in — expect `/`.
7. Open `/profile` as the new member — expect the "uzupełnij dane kontaktowe" prompt.

## Migration Notes

No schema change and no data migration. Existing accounts are untouched; member records holding a
live code keep working, and the codes issued before this slice remain valid — the URL is a new way
to deliver the same code, not a new credential.

Rollback is the ordinary redeploy of the previous artifact. Nothing in this slice writes state that
the previous version cannot read.

## References

- Roadmap slice: `context/foundation/roadmap.md` → `### S-17`, charter anchors `IR-01`–`IR-05`
- The claim path this narrows: `context/archive/2026-09-07-member-entity-and-accountless-members/plan.md`
- The approval removal this follows: `context/archive/2026-09-09-membership-pass-and-staff-booking/plan.md`
- Registration handler: `src/Application/Auth/AuthEndpoints.cs:259`
- Guard pattern: `src/app/src/app/core/auth/active-member.guard.ts`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: The API refuses a registration with no code

#### Automated

- [x] 1.1 Backend builds warning-free — 14bcdce
- [x] 1.2 Integration tests pass — 14bcdce
- [x] 1.3 No `ContactDetails` reference remains in `RegisterAsync` — 14bcdce

#### Manual

- [ ] 1.4 Codeless registration answers 400 and creates nothing
- [ ] 1.5 A valid code still claims the record with its bookings and karnet

### Phase 2: The register screen — a guarded door and two fields

#### Automated

- [x] 2.1 Frontend unit tests pass — a542f79
- [x] 2.2 Prettier + ESLint clean — a542f79
- [x] 2.3 Build succeeds inside the 550 kB initial-bundle budget — a542f79

#### Manual

- [ ] 2.4 `/register` with no query redirects to `/login`
- [ ] 2.5 `/register?invitationCode=…` while signed in redirects to `/`
- [ ] 2.6 The code field is prefilled and cannot be edited
- [ ] 2.7 A valid code registration lands on the dashboard with the record's data
- [ ] 2.8 A revoked or expired code lands on `/login` with a readable message
- [ ] 2.9 `email_taken` stays on the form and the typed password survives

### Phase 3: The invitation link, and the documents

#### Automated

- [x] 3.1 Frontend unit tests pass
- [x] 3.2 Prettier + ESLint clean
- [x] 3.3 Full backend suite still green

#### Manual

- [ ] 3.4 A copied invitation link opens a prefilled register form in a private window
- [ ] 3.5 The bare code is still readable in the panel
