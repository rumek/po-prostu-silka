# Test Environment Seed Data Implementation Plan

## Overview

A config-gated `TestDataSeeder` fills the development and staging databases with a realistic,
deterministic data set: 200 members (about 40 of them accountless with a claim code), 2 admins,
2 trainers, an exercise library, class types, a schedule covering four weeks back to four weeks ahead,
membership passes, bookings and a few active training plans. It runs at startup after `AdminSeeder`,
only in the `Development` and `Staging` environments, and only when `TestDataSeed:Enabled` is set.
With `TestDataSeed:Reset` also set, it first wipes every piece of domain data except the `AdminSeed`
account. There is no schema change, no migration, and no SPA change.

## Current State Analysis

- The only seeder is `AdminSeeder` (`src/Infrastructure/Identity/AdminSeeder.cs`). It is idempotent,
  guarded on "does this row exist", never on "is the table empty", and `Program.cs:331-347` calls it
  inside a `try/catch` so a seeding failure never kills startup before `/health` is mapped.
- Azure runs one App Service with `ASPNETCORE_ENVIRONMENT` unset, so it runs as `Production`. The code
  branches only on `Development` (`Program.cs:243`, `:351`) and on `Testing` (`Program.cs:404`, which
  maps the authorization probe endpoints). Any other environment name behaves exactly like
  `Production`, so switching Azure to `Staging` is safe.
- Every FK between domain tables is `DeleteBehavior.Restrict`. The exceptions are
  `PushSubscription → User` and `TrainingPlanItem → TrainingPlan`, which cascade. The wipe therefore
  has to delete in dependency order.
- The invariants the data must respect, and where they are enforced:
  - one active booking per (class, member): `BookingConfiguration.cs:68-70`, a filtered unique index
  - one active plan per member: `TrainingPlanConfiguration.cs:68-70`, a filtered unique index
  - unique member e-mail and unique access code: `MemberConfiguration.cs:78-88`
  - unique name for an active exercise and for an active class type: filtered unique indexes
  - capacity and pass rules: enforced only in `BookingProtocol` (`src/Application/Scheduling/BookingProtocol.cs:101-148`).
    The pass must cover the class's club-local date (`FindCoveringAsync`), active bookings carrying
    the pass id must stay below `EntryCount`, and the booking records `MembershipPassId`. The seeder
    writes through `DbContext` directly, so nothing in the database stops it from overbooking. It
    has to keep these rules itself.
- Roles: a trainer is an account in `User` + `Trainer` (`Register.cs:173`, `ChangeTrainerRole.cs:110`).
  The seeded admin is in `Admin` only (`AdminSeeder.cs:88`). Every account must have a `Member` row,
  or the membership claim refuses it everywhere (`AdminSeeder.EnsureMemberAsync`).
- An accountless member has `UserId = null` and a plaintext `AccessCode`. Codes come from
  `MemberAccessCode.Generate()` (8 characters from a 31-symbol alphabet) and last
  `MemberAccessCode.Validity` (14 days) (`src/Application/Members/MemberAccessCode.cs`).
- Integration tests share one Testcontainers SQL Server per `IntegrationCollection`
  (`tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:478-479`), with no reset between tests.

## Desired End State

- Locally: `TestDataSeed` values in `appsettings.Development.json` (`Enabled: false` by default). Set
  `Enabled: true` and `Reset: true` once, run the app, and the database holds the full data set.
  With `Reset` back to `false`, restarts leave the data alone.
- On Azure: `ASPNETCORE_ENVIRONMENT=Staging`, `TestDataSeed__Enabled=true` and
  `TestDataSeed__Password=<secret>`. An admin, a trainer and a member can log in with the shared
  password, and the admin's member list pages through 204 people.
- In `Production`, or with the flag off, the seeder logs one line and touches nothing.
- `dotnet test` includes a seeder test class that proves the gate, the counts, the S-16 invariants,
  idempotency without reset, and a reproducible reset.

### Key Discoveries:

- `AppDbContext : IdentityDbContext<ApplicationUser>` (`AppDbContext.cs:20-21`), so users and roles
  can be written in the same context and transaction as domain rows.
- `EnableRetryOnFailure` is on, so an explicit transaction has to run inside
  `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`. Otherwise EF throws.
- `ClubTime.AddLocalDays` and `ClubTime.Zone` (`src/Domain/Scheduling/ClubTime.cs`) are the only
  correct way to build club-local class times across the DST boundary. The seed window around late
  October crosses one.
- `MembershipPassRules` bounds: `EntryCount` 1..500, `TypeName` at most 100 characters
  (`src/Application/Members/MembershipPassRules.cs`).

## What We're NOT Doing

- No migration, no new table, no schema change. The reset trigger is a config flag, chosen over a
  version stamp that would need a table.
- No reset endpoint and no UI. Reset is `TestDataSeed:Reset=true`, turned off by hand afterwards.
  The user accepted the risk that a flag left on wipes the environment on every App Service recycle.
- No seeding in `Production` or `Testing`, whatever the flags say.
- No preservation of accounts created by hand during testing. Reset keeps only the `AdminSeed` account.
- No notifications. The seeder never touches the outbox except to empty it on reset. Seeded e-mails
  use the reserved `example.test` domain, so even a stray send cannot reach a real person.
- No YouTube ids beyond the three the user supplied (see the exercise catalogue in Phase 1 §2).
- No push subscriptions, no cancelled classes beyond a couple of illustrative ones, and no
  `AccountStatus.Pending` accounts (never produced since S-16).

## Implementation Approach

A single static seeder in Infrastructure, shaped like `AdminSeeder`: `SeedAsync(IServiceProvider, IConfiguration, IHostEnvironment, ILogger)`, called from `Program.cs` right after `AdminSeeder` in the same
guarded block. It takes the current time from the registered `TimeProvider` (`Program.cs:221`). The work
splits into three parts:

1. **Gate.** Environment in {`Development`, `Staging`}, `Enabled == true`, and `Password` non-empty.
   Any failure logs the reason at `Information` (flag off) or `Error` (flag on but refused) and returns.
2. **Wipe** (only when `Reset == true`). Delete in dependency order inside one transaction.
3. **Seed** (only when the sentinel is absent). The sentinel is the first seeded admin account,
   `admin1@example.test`. Generate everything in memory from a fixed-seed `Random` and save in one
   transaction, so a partial seed never leaves the sentinel behind.

The generator is a separate class (`TestDataGenerator`) that returns plain entity graphs and knows
nothing about the database. It is deterministic for a given seed and a given "today". The seeder
persists what it returns. This keeps the booking and pass rules testable as pure logic, and keeps
the seeder small.

## Critical Implementation Details

- **Password hashing cost.** `UserManager.CreateAsync` hashes with PBKDF2 once per account. At about
  164 accounts on a B1 plan that adds tens of seconds to cold start. Hash the shared password once
  with `IPasswordHasher<ApplicationUser>` and assign that hash to every seeded account, then insert
  users through the context with `NormalizedEmail`, `NormalizedUserName`, `SecurityStamp` and
  `ConcurrencyStamp` set. Use the registered `ILookupNormalizer`, so the values match what login
  looks up. Role links are `IdentityUserRole<string>` rows against role ids read from `Roles`.
- **Wipe order** (every FK is Restrict): `Bookings` → `TrainingPlanItems` → `TrainingPlans` →
  `Classes` → `ClassTypes` → `MembershipPasses` → `Exercises` → `OutboxMessages` →
  `PushSubscriptions` → `Members` except the AdminSeed account's member → Identity user-owned rows
  (`UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`) for users other than AdminSeed → `Users`
  except AdminSeed. Use `ExecuteDeleteAsync` so 200+ rows are not loaded to be deleted.
  If the AdminSeed account cannot be found, the wipe refuses. Otherwise it would delete every admin
  and lock the environment out of itself.
- **Booking generation must mirror `BookingProtocol`.** For each class, candidate members are the
  active members holding a pass whose `[ValidFrom, ValidTo]` covers the class's **club-local** date.
  Book only while the class's active bookings are below `Capacity` and the pass's active bookings
  are below `EntryCount`, recording `MembershipPassId`. Track both counters in memory as bookings
  are generated. Blocked members get no future bookings, matching `BlockMember`.
- **DST.** Build every `StartsAt` from a club-local wall-clock time with `ClubTime` helpers, never by
  adding `TimeSpan.FromDays` to a UTC instant. Otherwise the 18:00 class becomes 17:00 after
  25 October.

## Phase 1: The seeder and its tests

### Overview

Everything that runs: options, gate, wipe, generator, persistence, the `Program.cs` hook, the
development settings, and an integration test class on its own container.

### Changes Required:

#### 1. Options and settings documentation

**File**: `src/Infrastructure/TestData/TestDataSeedOptions.cs`, `src/Api/appsettings.json`,
`src/Api/appsettings.Development.json`

**Intent**: Name the four settings in one place and document them the way `AdminSeed` is documented.
The base file documents without values, and the Development file carries development-only values.

**Contract**: section `TestDataSeed` with `Enabled` (bool, default false), `Reset` (bool, default
false) and `Password` (string). `appsettings.json` gets a `"//TestDataSeed"` comment saying that
Azure supplies `TestDataSeed__Enabled` / `__Reset` / `__Password` as app settings, and that the
seeder only runs in `Development` and `Staging`. `appsettings.Development.json` gets `Enabled: false`,
`Reset: false` and a committed development password, with the same justification comment as
`AdminSeed`.

#### 2. The generator

**File**: `src/Infrastructure/TestData/TestDataGenerator.cs` (plus a `TestDataNames.cs` for the
static Polish name, street and city lists, and the exercise and class-type catalogues)

**Intent**: Produce the whole data set as in-memory entities, deterministically from
`(int seed, DateTimeOffset now)`, obeying every invariant listed under Current State Analysis.

**Contract**: a single entry point returning a `TestDataSet` record (users with their role names,
members, passes, class types, classes, bookings, exercises, plans with items). The content:

- **Staff:** `admin1@example.test` and `admin2@example.test` (role `Admin`), and
  `trener1@example.test` and `trener2@example.test` (roles `User` + `Trainer`). Each has a member row,
  Active, claimed.
- **Members (200):**
  - 160 with an account (`czlonek001@example.test` …, role `User`, `AccountStatus.Active`), each
    with a claimed member row.
  - 40 accountless (`UserId = null`). About half of them hold a live access code: unique,
    generated from the seeded `Random` over `MemberAccessCode`'s alphabet and length, expiring
    within `MemberAccessCode.Validity`.
  - About 5 members `MembershipStatus.Blocked`, and their accounts `AccountStatus.Blocked` where
    they have one.
  - Polish display names, phone numbers in `+48 5xx xxx xxx` form, and addresses on part of them.

  **Adapted during implementation.** Phone numbers are stored as nine bare digits (`5xxxxxxxx`,
  prefixes 5–8), because that is what `ContactDetails.TryNormalisePhone` stores for every number a
  user types. A `+48 …` string would be a shape no real row has. The access-code draw reads
  `MemberAccessCode.Alphabet`, which was `private` and is now `public` for this one reader, so the
  seeder cannot drift from the real alphabet.
- **Passes:** most members hold one or two. The mix includes valid monthly passes (8, 12 or
  30 entries), passes that expired within the window, a few used up (every entry consumed by past
  bookings), and a few members with no pass at all.
- **Class types:** about 6 (e.g. Yoga, Crossfit, Pilates, Zdrowy kręgosłup, TRX, Stretching), each
  with a default duration and capacity. One is inactive.
- **Classes:** club-local days from −28 to +28 relative to `now`, Monday to Saturday, 3–5 classes a
  day between 07:00 and 20:00, instructors spread across the two trainers. At least three future
  classes are filled exactly to capacity. Two classes are `Cancelled`, with their bookings cancelled.

  **Adapted during implementation.** The two cancelled classes keep their bookings **Active**.
  `CancelClass` (`src/Application/Scheduling/CancelClass.cs:21-26`) changes only the class's status,
  because cancelling the bookings would record that the members cancelled. Seed data that did
  otherwise would be a state the live app never produces (see roadmap Open Question 7).
- **Bookings:** generated under the rules in Critical Implementation Details. Past classes carry
  bookings too, which is what uses up entries.
- **Exercises:** about 25 across muscle groups (chest, back, legs, shoulders, arms, core, mobility),
  with Polish name, description, difficulty, equipment, preparation, starting position and
  execution. Two are inactive. Every exercise gets a `VideoId` picked by the seeded `Random` from
  the three YouTube Shorts the user supplied on 2026-09-22:
  `https://www.youtube.com/shorts/FR8qYjA8pKQ`, `https://www.youtube.com/shorts/qsmtzydAS_U` and
  `https://www.youtube.com/shorts/C-tRbRSBAoE`. The column stores the bare id, so each URL goes
  through `YouTubeVideoId.TryParse`, the same path the admin form uses. That parse has to succeed,
  or a Shorts URL would silently end up as a null `VideoId`.
- **Plans:** 8 active plans on distinct members (at least 2 of them accountless), each with 4–8
  items referencing active exercises, `Position` 0..n-1 and a mix of sets/reps/weight/rest/duration
  and notes. `AssignedByMemberId` is a trainer's or admin's member id. Add 2 archived plans on
  members who also hold an active one, so the one-active-plan index is exercised.

Ids are `Guid`s built from the seeded `Random`, so a reseed on the same day yields identical rows.

#### 3. The seeder

**File**: `src/Infrastructure/TestData/TestDataSeeder.cs`

**Intent**: Gate, optional wipe, sentinel-guarded seed. Same shape and the same "log, never throw
for configuration problems" stance as `AdminSeeder`.

**Contract**:
`public static Task SeedAsync(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment, ILogger logger)`.

- The gate is as described in Implementation Approach.
- The wipe runs in its own execution-strategy transaction, in the order given in Critical
  Implementation Details. It refuses if the `AdminSeed:Email` account is missing.
- The seed skips, logging `Information`, when `admin1@example.test` exists. Otherwise it hashes the
  password once, maps the generator's output onto Identity users and role links, and saves
  everything in one execution-strategy transaction.
- It logs counts on success (members, classes, bookings, plans). It never logs the password.

**Adapted during implementation.** The seed is a single `SaveChangesAsync`, with no explicit
transaction around it. One `SaveChanges` is already one transaction, and `EnableRetryOnFailure` retries
it as a unit, so the "no sentinel without the rest" guarantee holds without the extra ceremony. The wipe
keeps its explicit transaction inside `CreateExecutionStrategy().ExecuteAsync`, because it is fifteen
separate `ExecuteDeleteAsync` statements.

#### 4. Startup hook

**File**: `src/Api/Program.cs`

**Intent**: Call `TestDataSeeder.SeedAsync` right after `AdminSeeder.SeedAsync`, in the same scope.
It needs its own `try/catch` and its own logger category (`TestDataSeeder`), so that a seed failure
neither masks nor is masked by an admin-seed failure and never stops startup.

**Contract**: ordering is `AdminSeeder` then `TestDataSeeder`. The wipe depends on the AdminSeed
account existing, and the seed depends on the roles existing.

#### 5. Tests

**File**: `tests/po-prostu-silka.Tests/TestDataSeederTests.cs` (and a collection definition in the
same file)

**Intent**: Run the real seeder against a real SQL Server, on a container of its own. The wipe
would otherwise destroy the data the shared `IntegrationCollection` tests create.

**Contract**: a new `[CollectionDefinition("TestDataSeeder")]` over `IntegrationTestFixture`
(xUnit gives each collection its own fixture instance, and so its own container). The tests call
`TestDataSeeder.SeedAsync` directly with an in-memory configuration and a stub `IHostEnvironment`:

- `Refuses_in_Production_and_Testing`: nothing is inserted even with `Enabled` and `Reset` on.
- `Refuses_without_password`: nothing is inserted.
- `Seeds_the_expected_population`:
  - 204 members (200 non-staff), 164 accounts
  - 40 accountless members
  - 2 accounts in `Admin`, 2 in `Trainer`
  - 8 active plans
- `Bookings_obey_capacity_and_passes`, checked against the database. For every class, active
  bookings are at most `Capacity`, and at least one future class is exactly full. Every active
  booking has a `MembershipPassId` whose pass covers the class's club-local date, and for every
  pass, active bookings are at most `EntryCount`. At least one pass is fully used up.
- `Second_run_without_reset_changes_nothing`: row counts are identical.
- `Reset_reproduces_the_same_data_and_keeps_the_admin_seed_account`: seed, add a stray member,
  reset. The stray member is gone, the AdminSeed account and its member survive, and member ids
  match the first run.
- `Exercises_carry_a_supplied_video`: every seeded exercise's `VideoId` is one of
  `FR8qYjA8pKQ`, `qsmtzydAS_U` or `C-tRbRSBAoE`.
- `Seeded_account_can_log_in`: `POST` login as `trener1@example.test` with the configured password
  succeeds (this proves the shared-hash shortcut produces a hash Identity accepts).

**Adapted during implementation.** The reset keeps the AdminSeed account and its member, so after a
reset the database holds **205** members and **165** accounts: the 204 and 164 seeded, plus AdminSeed.
The population test counts seeded accounts and role members by the `@example.test` domain and asserts
205 members in total. The admin's member list likewise shows 205 people, not 204. Both refusal tests
first add a stray member and then assert that every row count is unchanged, which proves that neither
the wipe nor the insert ran. The file carries 9 test cases, because the environment refusal is a
theory over `Production` and `Testing`.

### Success Criteria:

#### Automated Verification:

- Solution builds warning-free: `dotnet build po-prostu-silka.slnx`
- All tests pass, the new seeder tests included: `dotnet test po-prostu-silka.slnx`
- No EF Core reference leaked into Domain/Application (build stays green, CS0234 guard)

#### Manual Verification:

- Local: with `Enabled`/`Reset` true, `dotnet run --project src/Api/po-prostu-silka.Api.csproj`
  seeds, and the log shows the counts. Startup stays under ~30 s on Docker SQL Server.
- Local: after resetting `Reset` to false, a restart logs "already present" and changes nothing.
- Local: `admin1@example.test` sees 204 members paged in `/admin/members`. `trener1@example.test`
  sees their classes and can book a member with a valid pass into a non-full class. A member with
  a used-up pass is refused with the "no free entry" message. A full class refuses.
- Local: a member account sees an active plan with exercise details, and an accountless member's
  code can be claimed via an invitation registration.

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to the next phase.

---

## Phase 2: Staging on Azure

### Overview

Flip the single Azure environment to `Staging`, supply the seed settings, reseed once and turn
`Reset` back off, then write down how to do it again.

### Changes Required:

#### 1. Deployment runbook

**File**: `context/deployment/deploy-plan.md`

**Intent**: Record the exact App Service settings and their order, so reseeding is a repeatable
operation rather than tribal knowledge.

**Contract**: a new section, "Test data (Staging only)", covering:

- `ASPNETCORE_ENVIRONMENT=Staging`
- `TestDataSeed__Enabled=true`
- `TestDataSeed__Password` (a secret, never in the repo)
- the reseed procedure: set `TestDataSeed__Reset=true`, which restarts the app, wait for the
  seed log line, then set `TestDataSeed__Reset=false`
- a warning that `Reset` left on wipes the database on every recycle
- a warning that the future production move must set `ASPNETCORE_ENVIRONMENT=Production` (or
  remove it) **before** real members are entered, since that is what makes the seeder refuse

#### 2. Contributor guidance

**File**: `AGENTS.md`

**Intent**: One short entry under "Database": the seeder exists, where it lives, its gate
(`Development`/`Staging` + flag), that `Reset` wipes everything except the AdminSeed account, and
that `Staging` was chosen over `Test` because `Testing` enables the probe endpoints.

**Contract**: a new bullet in `### Database`. No other section changes.

#### 3. Azure configuration (operational, no code)

**Intent**: Apply the settings above with `az webapp config appsettings set`, after Phase 1 is
merged and deployed.

**Contract**: the settings named in the runbook. The user confirms before anything is set, since
it restarts the only environment and wipes its data.

### Success Criteria:

#### Automated Verification:

- Deploy workflow on `main` succeeds (CI tests + publish + migrations)
- `GET /health` on the Azure app returns healthy after the settings change

#### Manual Verification:

- App Service log stream shows the seed counts after the reset restart
- After `TestDataSeed__Reset=false` and a manual restart, the log shows "already present" and the
  data is unchanged
- Log in on Azure as `admin1@example.test`, `trener1@example.test` and a `czlonekNNN@example.test`
  member with the shared password. Each lands on its dashboard with populated data.

---

## Testing Strategy

### Unit Tests:

- None separate. The generator's invariants are asserted against the persisted data in the
  integration tests, where the database's filtered unique indexes also get a say.

### Integration Tests:

- The eight tests under Phase 1 §5, on a dedicated Testcontainers SQL Server.

### Manual Testing Steps:

1. Local seed with reset, then inspect the counts in the log.
2. Restart without reset and confirm nothing changes.
3. Walk admin, trainer, member and accountless-claim paths as listed in Phase 1 Manual Verification.
4. Repeat the log-in checks on Azure after Phase 2.

## Performance Considerations

About 204 members, about 250 classes, 1–2k bookings and about 400 passes are inserted in one
`SaveChanges`, which EF batches. The single password hash avoids about 164 PBKDF2 runs. The seed
blocks startup, which is acceptable on B1: App Service's start timeout is minutes and the seed runs
only when absent or on reset.

## Migration Notes

None. There is no schema change. Rollback is removing the app settings (or redeploying the
previous artifact). Seeded rows stay until the next reset or a manual wipe.

## References

- Change notes: `context/changes/test-environment-seed-data/change.md`
- Pattern to follow: `src/Infrastructure/Identity/AdminSeeder.cs`
- Booking rules to mirror: `src/Application/Scheduling/BookingProtocol.cs:101-148`
- Roadmap item: S-24 in `context/foundation/roadmap.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: The seeder and its tests

#### Automated

- [x] 1.1 Solution builds warning-free: `dotnet build po-prostu-silka.slnx`
- [x] 1.2 All tests pass, the new seeder tests included: `dotnet test po-prostu-silka.slnx`
- [x] 1.3 No EF Core reference leaked into Domain/Application (build stays green, CS0234 guard)

#### Manual

- [ ] 1.4 Local seed with Enabled/Reset logs counts; startup under ~30 s
- [ ] 1.5 Restart without Reset logs "already present" and changes nothing
- [ ] 1.6 Admin sees 204 members paged; trainer books/is refused as the rules say
- [ ] 1.7 Member sees an active plan; an accountless member's code can be claimed

### Phase 2: Staging on Azure

#### Automated

- [ ] 2.1 Deploy workflow on `main` succeeds
- [ ] 2.2 `GET /health` on Azure returns healthy after the settings change

#### Manual

- [ ] 2.3 Log stream shows seed counts after the reset restart
- [ ] 2.4 With Reset off, a restart logs "already present" and data is unchanged
- [ ] 2.5 Admin, trainer and member log in on Azure and see populated dashboards
