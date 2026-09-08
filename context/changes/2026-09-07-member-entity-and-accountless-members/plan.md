# S-14: Member as a first-class entity, independent of the account

## Context

Today a person exists in the system only as an ASP.NET Core Identity account. `ApplicationUser` is
both "a login" and "a person in the club": `Bookings.MemberUserId`, `TrainingPlans.MemberUserId`,
`TrainingPlans.AssignedByUserId` and `Classes.InstructorUserId` all point straight at
`AspNetUsers.Id` (`src/Domain/Scheduling/Booking.cs:50`, `src/Domain/Training/TrainingPlan.cs:37,56`,
`src/Domain/Scheduling/Class.cs:148`).

That makes an ordinary gym situation impossible. The club has people who train there but have never
registered — signed up at the desk, walk-ins, people who simply do not want an account. The admin
cannot record them, cannot assign them a plan, cannot sign them up for a class. And when such a
person later does want an account, there is no way to attach it to the history the club already
recorded for them.

This change splits the two concepts. `AspNetUsers` goes back to being only a login. A new `Member`
entity becomes the person in the club, and everything club-shaped points at it. `Member.UserId` is a
nullable link to an account — `NULL` means "no login". A person joins an account to their existing
record by entering a **member code** issued by the admin, during registration, which is what
preserves their booking and plan history.

## Locked decisions

| Decision | Choice |
| --- | --- |
| Model | Separate `Member` entity (Guid PK); `Member.UserId` → `AspNetUsers.Id`, nullable, unique when set |
| Repointed FKs | `Bookings.MemberId`, `TrainingPlans.MemberId`, `TrainingPlans.AssignedByMemberId`, `Classes.InstructorMemberId` |
| Stays on the account | `PushSubscriptions.UserId` (a device belongs to a login), all Identity tables |
| Status | New `Member.Status` owns club membership and works without an account; `ApplicationUser.Status` keeps owning login and registration approval |
| Claiming | Optional "kod klubowicza" field on the existing registration form |
| Admin scope | Create / edit / block a record, generate + revoke the code, assign plans, book a class on someone's behalf |

## Decisions I made for you — override any of these at approval

1. **The new enum is `MembershipStatus`, not `MemberStatus`.** The SPA already exports `MemberStatus`
   for `AccountStatus` names (`core/admin/member-admin.models.ts:23`), and the admin list will show
   both statuses side by side.
2. **`MembershipStatus.Active = 0`, `Blocked = 1`** — the opposite default from `AccountStatus`, where
   `Pending = 0`. A record the admin typed in is vetted by construction; blocking must be an explicit
   act. There is no membership-level `Pending`: "not yet approved" already lives on the account.
3. **Accountless *instructors* stay unsupported in this slice.** The FK move makes one representable,
   but the `Trainer` role lives in Identity and a record with no account can hold none.
   `ValidateInstructorAsync` keeps requiring an active account holding `Trainer`. The roadmap already
   parks the trainer question (Open Question 3), so closing it is a follow-up.
   **Say the word and I will add `Member.IsTrainer` in Phase 6 instead** — it is roughly one column,
   one query and one validation branch.
4. **Contact details move to `Member`, they are not duplicated forever.** Two mutable copies of a
   phone number is the drift `ContactDetails` exists to prevent. Phases 1–6 write both; Phase 7 flips
   the reads and stops writing the copy.
5. **The access code is stored in plaintext**, forced by "admin reveals the code". Hashing it would be
   strictly safer and would make reveal impossible. If reveal is negotiable, say so — showing the code
   exactly once at generation is the better design.
6. **This is a new milestone.** Every slice in M-1 is `done` and its "Done when" is satisfied. Nothing
   in `prd.md` or `prd-v2.md` describes an accountless member, so this is new product scope. Close
   M-1, open **M-2**, write a short PRD addendum.

## Phases

Every phase leaves the app building, `dotnet test` and `npm test` green, and independently deployable.
The ordering is expand/contract, which `.github/workflows/deploy.yml:55` requires in as many words —
"schema is always >= code, never behind it" — because CI applies migrations *before* the new artifact
ships, so the previous artifact must keep running against the new schema.

### Phase 0 — Change folder, roadmap, PRD addendum

`/10x-new` → `context/changes/2026-09-07-member-entity-and-accountless-members/`. Close M-1 and open
M-2 in `context/foundation/roadmap.md`. Never write to `context/archive/`.

### Phase 1 — `Member` exists and is backfilled; nothing reads it

Create `src/Domain/Members/Member.cs` (`Guid Id`, `string? UserId` + read-side `User` nav,
`DisplayName`, `Email`, five contact fields, `Status`, `CreatedAt`, `AccessCode`,
`AccessCodeExpiresAt`, `ClaimedAt`, `ConcurrencyStamp`), `src/Domain/Members/MembershipStatus.cs`, and
`src/Infrastructure/Persistence/Configurations/MemberConfiguration.cs` — lengths reused from
`ContactDetails`' constants, `ConcurrencyStamp` as a concurrency token exactly as
`TrainingPlanConfiguration.cs:38` does, and four indexes:

- `IX_Members_UserId` UNIQUE, filter `[UserId] IS NOT NULL`
- `IX_Members_AccessCode` UNIQUE, filter `[AccessCode] IS NOT NULL`
- `IX_Members_Email` UNIQUE, filter `[Email] IS NOT NULL`
- `IX_Members_Status`

The `IS NOT NULL` term is load-bearing, not decoration: SQL Server treats NULLs as **equal** for
uniqueness, so without it the second accountless member is rejected.

Migration 14 `AddMembers` — create the table, then one guarded `INSERT … SELECT` backfilling a record
per `AspNetUsers` row (`Status = CASE WHEN u.[Status] = 2 THEN 1 ELSE 0 END`: a blocked account
becomes a blocked membership, Pending and Active both become Active, because the account gate is what
holds a pending member back). `Down` drops the table, lossy for accountless rows — say so in the file.

Three producers must be updated in this phase so no account ever exists without a record. **All three
are load-bearing for Phase 3:** `AdminSeeder.cs` (the seeded admin), `AuthEndpoints.RegisterAsync`
(with the same log + `DeleteAsync` + 500 rollback the role-assignment failure already gets), and
`IntegrationTestFixture.EnsureUserAsync` — the single chokepoint every test seeds through. Add a
`CreateMemberAsync` helper there for accountless fixtures.

### Phase 2 — Admin manages an accountless member

On the existing `/api/admin/members` group: `POST /` (create), `PUT /{memberId:guid}` (edit),
`POST /{memberId:guid}/block` and `/unblock` (membership block). **Every route on this group
re-addresses by `Guid memberId`**, including `approve` and `roles/trainer`, which then load the linked
account and answer 409 `no_account` when there is none — one key for the whole screen is worth the
churn. New `IMemberStore` seam in Application, `MemberStore` in Infrastructure, registered in
`Program.cs`. Reuse `ContactDetails.TryCreate` and its failure vocabulary verbatim; this is its third
caller.

The membership block copies `MemberAdminEndpoints.BlockAsync`'s shape exactly: rotate the member's
`ConcurrencyStamp`; when an account exists, rotate its `SecurityStamp` **and** `ConcurrencyStamp`
(that is what ends a live session) and keep the `is_admin` refusal; run
`CancelActiveFutureForMemberAsync`; one save; `conflict` on a lost race.

`MemberSummary` reshaped to carry `Guid Id`, `UserId`, both statuses (account status null when there
is no account), roles and `HasAccessCode`. `PendingMember` gains `MemberId`. `MemberQuery` and
`PendingMemberQuery` project from `db.Members`, left-joining `db.Users`. SPA: `member-admin.models.ts`
/ `.service.ts`, the members screen, and a new `features/admin/members/member-form.*` at
`/admin/members/new` and `/admin/members/:id` (`'new'` before `':id'`, lazy, `[authGuard, adminGuard]`).
No new nav entry — `/admin/members` is already in `app.html` and `features/more/more.html`.

**Adapted during implementation (Phase 2).**

- **Contact details are all-or-nothing, not required.** The plan said to reuse
  `ContactDetails.TryCreate` verbatim, which requires all five fields. Applied literally that makes
  the slice unusable for its own case: an admin recording a walk-in at the desk would have to produce
  a full postal address before the club may write the person down. The block is therefore optional,
  and validated by `TryCreate` unchanged the moment any of the five is supplied. The validator, its
  failure codes and the ordering are untouched — only whether it is called.
- **The list filter is a `MemberListFilter`, not `AccountStatus?`.** With two statuses, filtering on
  the account's alone would have hidden every accountless member behind three of the four chips, and
  a blocked member with no login would have appeared under none of them. The four positions
  (`Pending`, `Active`, `Blocked`, `WithoutAccount`) are the states an admin thinks in; they are
  computed server-side and deliberately overlap the two underlying enums.
- **`GET /{memberId}` and `MemberDetail` were added.** The plan implied a read behind the edit form
  without naming one. It is a separate contract from `MemberSummary` so the list does not ship
  everybody's home address to render a table.
- **`IMemberStore` landed in Phase 1, not Phase 2.** Registration had to create a member in that
  phase, and it cannot touch EF Core from Application.
- **Unblock became idempotent.** Before S-14 an already-active account answered 200 and a *pending*
  one answered 409 `not_blocked` ("approve is the action you want"). Membership has only two states,
  so that second case cannot arise: "not blocked" is "already active". Reporting an error for a no-op
  would make an admin's double-click look like a failure, so the reason is gone from the API and from
  the SPA's model, and unblock now mirrors block exactly.

### Phase 3 — `Member.Status` enters the authorization contract

Policies read cookie claims, not the database, so the answer is a second claim.
`AppUserClaimsPrincipalFactory` (already in Infrastructure, so it may read `AppDbContext`) mints
`member_id` and `member_status` off one indexed read on `IX_Members_UserId`; claim type names go beside
`StatusClaimType` in `src/Domain/AuthorizationPolicyNames.cs`; all three policies in
`AuthorizationPolicies.cs` additionally require `member_status = Active`. `CurrentUser` gains both
fields; the SPA's auth models and three guards follow.

Propagation needs nothing new: the claim re-mints on the 2-minute
`SecurityStampValidatorOptions.ValidationInterval` and on `POST /api/auth/refresh`, and because
Phase 2's block rotates the linked account's security stamp, a membership block bites on exactly the
same schedule an account block does.

> **This is the riskiest phase.** Once the claim is required, an account with no `Member` row fails
> every policy — including `Admin`, which means the club can be locked out of its own app. The three
> producers from Phase 1 are what make that unreachable, and they must be verified against a restored
> copy of production before this deploys, not against the test container. Deliberately **no**
> "absent ⇒ treat as Active" fallback: that is a permanent authorization bypass hiding behind a
> data-integrity assumption. If you want a safety net, make it a startup log-and-count.

**Adapted during implementation (Phase 3).**

- **`PUT /api/profile` now writes the member's contact details too.** The plan assigned the
  contact-detail dual-write to "Phases 1–6" without naming the member's own profile screen as one of
  the writers. Left as it was, a member correcting their phone number would update the account and
  leave the club's record stale — invisible until Phase 8's read flip resurrected the old value. Both
  copies move in the one `SaveChangesAsync` `UserManager.UpdateAsync` already issues.
- **`GET /api/auth/me` reads the membership rather than taking it from the claim.** The claim is up to
  one validation interval stale, and seeing past exactly that staleness is what `/me` is for — the SPA
  calls it on every cold load to decide where to route.
- **`CurrentUser.MemberId` / `MembershipStatus` are nullable on the wire.** They are null exactly when
  the account has no member row, which is the tripwire state; filling in a default would hide the
  failure the policies exist to surface.
- **A membership block does not end a live session instantly, and the tests now say so.** The plan
  said a block "bites on exactly the same schedule an account block does", which is true and is the
  point — but that schedule is the security-stamp validation interval (2 minutes) or an explicit
  `/api/auth/refresh`, not the moment of the block. `MemberBlockPolicyTests` pins that bound rather
  than asserting an immediate 401 the product has never done.

**Still outstanding from this phase's verification:** the plan's step 5 — running the Phase 1 backfill
against a restored copy of production and asserting zero accounts without a member — has NOT been
done, and cannot be from here. It must happen before this phase is deployed.

### Phase 4 — Repoint the FKs, dual-write

Migration 15 `AddMemberForeignKeys`: add **nullable** `Bookings.MemberId`, `TrainingPlans.MemberId`,
`TrainingPlans.AssignedByMemberId`, `Classes.InstructorMemberId`; backfill by joining `Members` on
`UserId`; add the `Restrict` FKs; add the new indexes **alongside** the old ones, the two unique ones
filtered `[Status] = 0 AND [MemberId] IS NOT NULL` for the NULL-equality reason above.

Entities and configurations gain the new properties while keeping the old. Write paths set **both**
columns; reads still use the old ones. Wire contracts are unchanged, so the SPA is untouched. This
phase exists solely so rollback across Phase 5 is safe.

**Adapted during implementation (Phase 4).**

- **The read-side navigations were renamed, not left alone.** `Booking.Member`, `TrainingPlan.Member`
  / `AssignedBy` and `Class.Instructor` became `MemberAccount` / `AssignedByAccount` /
  `InstructorAccount`, and the new names now hold the `Member` navigations. This was forced: both
  entities carry a `DisplayName`, so leaving the old names on the new type let every projection
  compile while silently reading from the wrong table. The compiler could not catch it and the tests
  would not have either until the data diverged.
- **`ClaimsPrincipalExtensions.GetMemberId` landed in Phase 4, not Phase 5.** The parallel write needs
  the caller's member id at the same three places the reads will.
- **`ValidateInstructorAsync` now returns the instructor's member id.** The alternative was a second
  lookup per class write for a value the validation had already resolved. Its rule is unchanged — an
  instructor still needs an active account holding Trainer.
- **The backfill runs before the unique indexes are created**, which is ordering EF does not produce
  on its own: an index built over a column that is still NULL everywhere sees every row as equal and
  the CREATE fails.
- **`DualWriteTests` was added and is deliberately temporary.** The parallel write is invisible —
  nothing reads the new columns yet — so a path that forgot one would pass every other test in the
  suite until the reads moved. The file says to delete it when the old columns go.

### Phase 5 — Reads flip to `MemberId`

The principal already carries `member_id`, so add a `GetMemberId(this ClaimsPrincipal)` extension
(pure BCL, no query) and let the six call sites that use `userManager.GetUserId(principal)` for a
member — `BookingEndpoints` ×3, `MyPlanEndpoints` ×2, `TrainingPlanEndpoints` ×1 — use it instead. The
two in `PushEndpoints` keep the user id. The "member id never comes from the body" rule survives.

The store and query seams change `string memberUserId` → `Guid memberId` throughout.
`TrainingPlanQuery.GetAssignableMembersAsync` (line 33) becomes `db.Members` filtered on
`MembershipStatus.Active` — **this is the change that makes accountless members assignable**.
`TrainerQuery` joins through `Members`. `ClassChangeNotification` resolves recipients through
`Member → UserId`, so `ClassBooking` carries `string? UserId` beside `Guid MemberId`: an accountless
member gets email when one is on file and no push at all. Do not "fix" that by keying push on
`MemberId` — a device belongs to a login.

Wire contracts break here (`ClassBooking.MemberUserId`, `TrainingPlan*.MemberUserId`,
`AssignableMember.Id`, `ScheduledClass.InstructorUserId`) and the SPA mirrors change in the same
commit. That is safe only because the SPA ships inside the API's own `wwwroot` as one artifact — do
not split this phase across two deploys. Dual-write stays on.

**Adapted during implementation (Phase 5).**

- **The class's instructor is submitted as a MEMBER id, not an account id.** The plan named
  `ScheduledClass.InstructorUserId` as a contract that breaks, but stopped short of the request side
  and of `/api/admin/trainers`. Leaving those on account ids would have made the scheduling surface
  the one place where the SPA still speaks Identity. `TrainerSummary.Id`, `ClassRequest`, and
  `ScheduledClass` all carry member ids now, and `ValidateInstructorAsync` resolves the account behind
  the member. **The rule is unchanged** — an instructor still needs an active account holding Trainer,
  and an accountless member is refused as `unknown_instructor`.
- **The assignable-member rule is "active membership AND, if there is a login, an approved one".** The
  plan said to drop the `AccountStatus.Active` filter outright. Applied literally that also let a plan
  be assigned to an account that self-registered minutes ago and has not been vetted — a loosening
  nobody asked for and which a test caught. The predicate now lives in one place
  (`TrainingPlanQuery.Assignable`) shared by the picker and by the write-side validation, so the two
  cannot drift into disagreeing about who is eligible. `FindMemberStatusAsync` became
  `IsAssignableAsync` for the same reason.
- **`ClassBooking` carries `UserId` beside `MemberId`**, as planned, and the fan-out now `continue`s
  past a member with no account rather than looking up push subscriptions that cannot exist.
- **The blocked-member booking cascade is keyed on the member**, so it finally covers an accountless
  member — the case it could not reach while bookings were keyed on the login.
- **`IntegrationTestFixture.UserIdOfMemberAsync` was added and is deliberately temporary.** Tests that
  insert entities directly must still populate the legacy account column, because it is still
  NOT NULL with a live foreign key. It says in its own doc comment that it goes away with those
  columns.

### Phase 6 — Access code and the claim at registration

`src/Application/Members/MemberAccessCode.cs`, pure BCL beside `ContactDetails`: 8 characters from an
unambiguous uppercase alphabet (no `0/O`, `1/I/L`), stored normalised, shown as `XXXX-XXXX`, and
normalised on input so a member who types the dash or lowercases still succeeds. Collisions retry
through `IUnitOfWork.TrySaveAsync` + `DiscardChanges` — the seam that exists for exactly this shape,
"a write protected by a unique index as well as a concurrency token". `AccessCodeExpiresAt` is a
stored column, not a derivation, so changing the default never retro-extends a live code. Revocation
nulls both fields; single use falls out of consumption nulling the column.

Admin: `POST /{memberId:guid}/access-code` (409 `has_account` when linked), `GET` as a **separate**
route so the member list never ships every live code, `DELETE` to revoke.

`RegisterRequest` gains optional `MemberCode`. Failure reasons: `invalid_member_code` (400, bad
format) and `unknown_member_code` (409) — one code for "no such member", "expired" and "revoked"
alike, exactly as `ResetPasswordFailure.invalid_token` collapses its causes, because "expired" would
confirm the code once existed. Validated before the duplicate-email check and long before
`CreateAsync`, so a bad code creates nothing. On a successful claim the club's `DisplayName` wins and
the submitted one is ignored (the S-13 rule — a member may not rename themselves), while the submitted
contact details do overwrite, because the person is the better source for their own phone and address.
The claimed account is still created `Pending`; the code proves the club knows them, not that login is
approved. Account creation and the link are two saves, ordered so a failure leaves an unlinked account
with the code still live and retryable, never a consumed code with no account.

### Phase 7 — Admin books on a member's behalf

Extract `BookAsync`'s body into `TryBookAsync(classId, memberId, …)` with `MaxAttempts = 10`, the
per-attempt re-read, every refusal, the stamp rotation, `TrySaveAsync`, `DiscardChanges` and the
`conflict` exhaustion **exactly as they are**. The member route passes the claim; the new admin route
`POST /api/admin/classes/{classId}/bookings` passes `request.MemberId`. One method, two callers, is
what guarantees the no-overbooking protocol is literally the same one. Admin-only pre-checks before
the loop: unknown member → 404, blocked → 409 `member_blocked`. Response is the `ScheduledClass` as it
now stands, like the member path, so the booking panel updates in place. Also here: the trainer's
member picker marks accountless people "bez konta", so nobody wonders why the plan never appears on
someone's phone.

### Phase 8 — Contact details flip, schema tightens

`BuildCurrentUserAsync` and `ProfileEndpoints` read and write the `Member`; the dual-write of contact
details and of the legacy FK columns stops. Migration 16 `RequireMemberForeignKeys`: make the four new
columns `NOT NULL`, **make the four legacy columns nullable** (easy to forget, and without it every
INSERT fails the moment the code stops writing them), and rebuild the two filtered unique indexes with
the plain `[Status] = 0` filter.

### Phase 9 — The destructive drops, one release later

Migration 17 `DropLegacyUserColumns`: drop the four legacy FK columns and
`AspNetUsers.Street/HouseNumber/PostalCode/City` (leave `PhoneNumber` — Identity's own column). `Down`
re-adds them nullable and backfills from `Members`, lossy for accountless rows, the precedent
`20260902165516_DropDeadClassColumns` already set. No code change; this phase exists only so the
rollback of Phase 8's artifact still finds its columns.

## Tests

New: `MemberEndpointTests` (accountless create/edit/block, including "blocking an accountless member
cancels their future bookings and touches no account"), `MemberBlockPolicyTests` (blocked membership +
`/refresh` ⇒ 403 while the account is still Active), `MemberAccessCodeTests` (format and normalisation,
no DB), `AdminBookingEndpointTests` — including the no-overbooking race run through the **admin**
route, which is the proof that the extraction in Phase 7 really did keep one loop.

Changed: `RegisterEndpointTests` (a Member is created; the claim inherits bookings *and* the active
plan; single-use, expired, revoked, already-linked), `MemberAdminEndpointTests` (new key and shape),
`BookingEndpointTests`, `MyPlanEndpointTests`, `TrainingPlanEndpointTests`, `ClassEndpointTests`,
`ClassCancellationTests`, `AuthEndpointTests` (new claims), `ProfileEndpointTests` (edits land on
`Members`), `PushEndpointTests` (push stayed account-keyed), `IntegrationTestFixture`.

## Verification

1. `docker compose up -d`; from `src/`: `dotnet build`, `dotnet run`, `GET /health` → healthy (a real
   DB connection, per AGENTS.md).
2. From the repo root: `dotnet test` — integration tests against real SQL Server via Testcontainers,
   so the filtered unique indexes and the booking concurrency protocol are genuinely exercised. Docker
   must be running.
3. From `src/app/` on Node 22+: `npm test` and `npm run quality:check`.
4. Migration reversibility, which the deploy rule depends on and the tests do not cover: for each new
   migration, `dotnet ef database update <previous>` and forward again against the docker database.
   Read `dotnet ef migrations script --idempotent` the way the deploy workflow does.
5. Before Phase 3 specifically: run the Phase 1 backfill against a **restored copy of production** and
   assert zero accounts without a `Member`.
6. Manual end-to-end: admin creates a member with no account → assigns a plan → books them into a
   class → generates a code → register in a private window with that code → the new account lands on
   that member's history, with the booking and the plan both there.

## Open risks

- **`/register` has no rate limit**, deliberately (documented in `RegisterAsync`). The code now rides
  on that endpoint. 8 characters over a 31-symbol alphabet against a handful of live, expiring,
  single-use codes makes guessing uneconomic, so I did not add a limiter — but a second
  `RateLimitPolicies` entry is roughly 15 lines if you want it.
- **Two display names during Phases 1–7.** `ClaimTypes.GivenName` is minted from
  `ApplicationUser.DisplayName`, so that has to move before the column could ever be dropped.
- **The backfill is a data migration inside a schema migration** — one unbatched `INSERT..SELECT` on
  Azure SQL Basic (5 DTU). Fine at one club's scale; it would not be at ten.
- **One extra indexed read per claims mint**, at sign-in and every 2 minutes per signed-in user. Same
  order as the security-stamp check already running there, but it is the second thing in this app that
  scales with concurrent users rather than member count.
