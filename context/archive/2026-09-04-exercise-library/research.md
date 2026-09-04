---
date: 2026-09-04T17:40:50+02:00
researcher: Karol Rumianowski
git_commit: 5ebb088caa6971b2010307cb3dd9cac1b12bc1a3
branch: main
repository: po-prostu-silka
topic: "Exercise library (S-10): admin-only CRUD screens, muscle group, YouTube video with thumbnail"
tags: [research, codebase, exercise-library, admin-crud, angular, ef-core, navigation]
status: complete
last_updated: 2026-09-04
last_updated_by: Karol Rumianowski
---

# Research: Exercise library (S-10)

**Date**: 2026-09-04 17:40 +02:00
**Researcher**: Karol Rumianowski
**Git Commit**: `5ebb088caa6971b2010307cb3dd9cac1b12bc1a3`
**Branch**: `main`
**Repository**: `po-prostu-silka` (`https://github.com/rumek/po-prostu-silka`)

## Research Question

What does the codebase already establish that the `exercise-library` change must follow? The change
adds an admin-only exercise library: a list screen (name, description, muscle group, YouTube
thumbnail), a detail screen with an edit action and every field, and a create screen.

> **Scope update, 2026-09-04 (after this research was written).** The top-menu entry the original
> notes asked for is **dropped from this change**. The library follows the existing convention
> instead — admin screens are reached by URL or from a cross-link on another admin screen — until
> S-12's dashboards exist. Section 2's navigation findings are kept below as the record of why that
> was a real decision rather than an oversight, and as the contract for whoever adds the entry later.

Scope chosen by the user: the **full vertical pattern** (Domain → EF → migration → endpoints →
authorization → Angular service → screens → tests → routing/nav), with evidence prepared for four
open design decisions: muscle-group modelling, YouTube URL vs videoId, delete vs deactivate, and
screen/navigation shape.

## Summary

1. **There is one canonical admin-CRUD vertical to copy: `ClassType` (S-05).** Every layer of it —
   entity, EF configuration with a filtered unique index, minimal-API group under the `Admin`
   policy, `IXQuery`/`IXStore` seams keeping EF Core out of `Application`, Angular list + shared
   create/edit form, integration tests — is intact and idiomatic. `Exercise` should be a near-copy,
   not a new pattern.
2. **Nothing about this domain exists yet.** No `Exercise`, `TrainingPlan`, muscle group, image or
   video artefact anywhere in `src/` or in any archived change. The exercise library is greenfield
   inside a mature convention.
3. **Two genuinely new things** have no precedent to copy and must be decided in the plan, not
   assumed: (a) **displaying an image** — the frontend contains no `<img>`, no `NgOptimizedImage`,
   no sanitizer usage at all; (b) **interpreting a URL** — no code in `src/` ever parses a URL
   (`Uri.TryCreate` appears nowhere); the one URL-shaped column is stored raw. A third — a
   **top-menu entry for an admin screen**, which every previous admin slice deliberately avoided —
   was raised by this research and then dropped from scope (see the note above), so the change now
   keeps that convention intact.
4. **The PRD's "no standalone exercise library browsing" is about members, not admins.** FR-018
   requires the admin to manage the library, which is exactly these three screens. The member-facing
   entry point stays inside the training plan (FR-020, S-11). The plan must state this explicitly,
   because the sentence in `## Non-Goals` reads like a contradiction on first pass.
5. **The dominant modelling instincts here are:** enum-as-int for closed code-defined sets,
   soft-deactivation over deletion, optional strings normalised to `null` (never `""`), server and
   client validators mirroring each other with the same constants, and structured
   `{ "reason": "..." }` failure bodies mapped onto the responsible form control.

## Detailed Findings

### 1. Backend vertical: the `ClassType` template

**Domain entity** — `src/Domain/Scheduling/ClassType.cs:28-66`

- A POCO with public get/set on every property. No factory, no constructor, no guard clauses, no
  value objects. Invariants live in the endpoint's `Validate()` and in the EF configuration, not in
  the entity.
- `Guid Id { get; set; }` — the PK, assigned explicitly by the endpoint
  (`ClassTypeEndpoints.cs:181`), not defaulted in the entity.
- Required string: `string Name { get; set; } = string.Empty;` (line 36) — default-initialised, no
  data annotations.
- Optional string: `string? Description { get; set; }` (line 43), documented as "null rather than
  empty string when absent, so 'no description' has one representation".
- `bool IsActive { get; set; } = true;` (line 62) with an explicit comment choosing `bool` over an
  enum because there are exactly two states.
- `DateTimeOffset CreatedAt` (line 65), set by the caller from `TimeProvider`.
- Heavy XML docs carry the *reasoning* (notably the "copied template value vs resolved-by-reference
  identity value" asymmetry). This is house style and worth matching.

**EF configuration** — `src/Infrastructure/Persistence/Configurations/ClassTypeConfiguration.cs:7-41`

```csharp
builder.ToTable("ClassTypes");
builder.HasKey(x => x.Id);
builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
builder.Property(x => x.Description).HasMaxLength(1000);      // no IsRequired -> nullable column
builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
builder.HasIndex(x => x.Name).IsUnique()
       .HasFilter("[IsActive] = 1")
       .HasDatabaseName("IX_ClassTypes_Name_Active");
```

- Nullability comes from the CLR type alone; there is no `IsRequired(false)` call anywhere.
- Max-length constants are **duplicated** in `ClassTypeEndpoints` (`MaxNameLength`,
  `MaxDescriptionLength`, `ClassTypeEndpoints.cs:108-111`) with a comment demanding they stay in
  step. There is no single source of truth — a known, accepted wart.
- `AppDbContext` registration is a one-liner: `src/Infrastructure/Persistence/AppDbContext.cs:27` —
  `public DbSet<ClassType> ClassTypes => Set<ClassType>();`. Configurations are auto-discovered
  through `ApplyConfigurationsFromAssembly`.

**Migrations** — `src/Infrastructure/Persistence/Migrations/`

Ten migrations to date, latest `20260903164629_AddBookings`. The ClassType one is
`20260902111715_AddClassTypes.cs`: `Up` creates the table + filtered index and adds the FK with
`ReferentialAction.Restrict`; `Down` (lines 85-101) is fully implemented. Commands, as recorded in
`context/archive/2026-09-02-class-type-definitions/plan.md:255-276`:

```
dotnet ef migrations add <Name> -p src/po-prostu-silka.csproj -o Infrastructure/Persistence/Migrations
dotnet ef database update -p src/po-prostu-silka.csproj --connection "<dev connection string>"
dotnet ef migrations script <Previous> <New>     # reversibility check
```

`--connection` is mandatory because `AppDbContextFactory` wins at design time and its placeholder
string is never meant to connect (CLAUDE.md). `AppDbContextModelSnapshot.cs` is regenerated and
committed with every migration.

**Application endpoints** — `src/Application/Scheduling/ClassTypeEndpoints.cs` (405 lines)

- Static class + `MapClassTypeEndpoints(this IEndpointRouteBuilder)` extension (line 113),
  registered in `Program.cs:252` beside its six siblings (lines 247-253). No controllers exist
  anywhere in this codebase.
- Group: `app.MapGroup("/api/admin/class-types").WithTags("Schedule").RequireAuthorization(AuthorizationPolicyNames.Admin)`
  (lines 115-117) — **authorization at the group, never per route**, so a route added later cannot
  ship unguarded.
- Routes (lines 119-128): `GET /`, `GET /{id:guid}`, `POST /`, `PUT /{id:guid}`,
  `POST /{id:guid}/deactivate`, `POST /{id:guid}/activate`. **No `DELETE`** — and a test pins its
  absence.
- DTOs are records **in the same file**: `ClassTypeSummary` (lines 23-30), `ClassTypeRequest`
  (42-46, shared by create and edit, deliberately without `IsActive`), `ClassTypeFailure(string
  Reason)` (58). Failure reasons are catalogued in the XML doc (line 53).
- Validation is a hand-rolled `private static IResult? Validate(ClassTypeRequest)` (lines 318-351),
  called at the top of create and update. An explicit comment (314-317) rejects adding a validation
  library for a handful of fields. **No FluentValidation, no endpoint filters, no ProblemDetails.**
- Status conventions: `400` + `{reason}` for validation, `409` + `name_taken` for the uniqueness
  conflict, plain `Results.NotFound()` (no body) for 404, and `200 Ok(dto)` for create (not 201).
- `IUnitOfWork` (`src/Application/Persistence/IUnitOfWork.cs`) is injected and
  `SaveChangesAsync` called explicitly; no concurrency token on `ClassType` (documented as a
  single-admin shortcut, `ClassTypeEndpoints.cs:237-241`).

**Authorization** — `AuthorizationPolicyNames.Admin` lives in `src/Domain/AuthorizationPolicyNames.cs`
(Domain, so `Application` can name a policy without referencing Infrastructure); the builder that
turns names into policies is `src/Infrastructure/Authorization/AuthorizationPolicies.cs`. Two
policies exist: `ActiveMember` and `Admin` (= ActiveMember + Admin role).

**Persistence seams** — no generic repository. Two narrow interfaces declared **in Application**
next to the endpoints (`ClassTypeEndpoints.cs:375-404`):

- `IClassTypeQuery` — read side, returns `IReadOnlyList<ClassTypeSummary>` (DTOs, not entities);
  implemented by `src/Infrastructure/Scheduling/ClassTypeQuery.cs` with `AsNoTracking()` + a LINQ
  projection straight into the record, ordered `OrderByDescending(IsActive).ThenBy(Name)`.
- `IClassTypeStore` — write side: `FindAsync` (tracked, on purpose), `Add`, `IsNameTakenAsync`. It
  has **no `Remove`** and does not save; the endpoint commits through `IUnitOfWork`.
- Both registered scoped in `Program.cs:199-200`; they share the request's `AppDbContext` with
  `UnitOfWork`, which is what makes `Add` + `SaveChangesAsync` one write.

**Tests** — `tests/po-prostu-silka.Tests/ClassTypeEndpointTests.cs` (378 lines)

- `[Collection(nameof(IntegrationCollection))]`, constructor-injected `IntegrationTestFixture`.
- The fixture (`IntegrationTestFixture.cs:25-158`) starts one SQL Server Testcontainer
  (`mcr.microsoft.com/mssql/server:2022-latest`, same tag as `docker-compose.yml`) for the whole
  run, calls `Database.MigrateAsync()` **before** building `WebApplicationFactory<Program>` (the
  admin seeder needs the role tables), and seeds four fixed users (`ActiveAdminEmail`,
  `ActiveMemberEmail`, `PendingMemberEmail`, `BlockedMemberEmail`) through `UserManager`.
- **There is no DB reset between tests.** Collisions are avoided by generating unique data per test:
  `UniqueName(prefix) => $"{prefix}-{Guid.NewGuid():N}"` (`ClassTypeEndpointTests.cs:40`).
- Authenticated client: `await fixture.CreateAuthenticatedClientAsync(TestUsers.ActiveAdminEmail)`;
  `fixture.CreateClient()` is anonymous.
- Naming: `Snake_case_sentences` (`Admin_creates_a_type_and_gets_it_back`,
  `Second_active_type_with_the_same_name_is_409`), grouped by `// --- section ---` comments.
- Two reusable coverage idioms: a `TheoryData<string,string> EveryRoute` driving
  `Anonymous_is_401_on_every_route` / `Active_non_admin_is_403_on_every_route`, and a
  `TheoryData<object,string> InvalidRequests()` pairing each malformed payload with its expected
  `reason`.
- Response DTOs are re-declared as private records inside the test class (`ClassTypeBody`, lines
  27-34) rather than referencing the Application record.

### 2. Frontend vertical: routes, screens, services

**Routing** — `src/app/src/app/app.routes.ts:23-60`

```ts
{ path: 'admin/class-types', component: ClassTypes, canActivate: [authGuard, adminGuard] },
{ path: 'admin/class-types/new', component: ClassTypeForm, canActivate: [authGuard, adminGuard] },
{ path: 'admin/class-types/:id', component: ClassTypeForm, canActivate: [authGuard, adminGuard] },
```

- Guards compose as an array, never nest. The literal `new` segment **must** precede `:id`
  (commented twice in the file).
- Lazy `loadComponent` is reserved for calendar-heavy screens (`admin/classes`, `schedule`,
  `my-classes`); simple admin screens are imported eagerly and referenced with `component:`. An
  exercise library with no heavy dependency follows the eager `class-types` style.
- No route `data`/`title` convention exists.
- `app.routes.server.ts:1-8` prerenders everything (`{ path: '**', renderMode: RenderMode.Prerender }`),
  which is why **every guard early-returns `true` under `isPlatformServer`**
  (`core/auth/auth.guard.ts:20-22`, `admin.guard.ts:18-20`). No new guard is needed here — reuse
  `adminGuard`.

**Top navigation** — `src/app/src/app/app.html:6-34` (verified directly)

```html
@if (auth.isActive()) {
  <a routerLink="/schedule" routerLinkActive="is-active">Grafik</a>
  <a routerLink="/my-classes" routerLinkActive="is-active">Moje zajęcia</a>
}

@if (auth.isAdmin() && auth.isActive()) {
  <a routerLink="/admin/approvals" routerLinkActive="is-active">Zgłoszenia</a>
}
```

- The shell (`app.ts` + `app.html`) *is* the header; there is no separate layout component.
- The admin condition is `isAdmin() && isActive()` and a comment (lines 8-15) requires it to match
  `adminGuard` exactly — this is the fix from a real past bug
  (`context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:125-133`, F5), where a
  pending admin saw a link that bounced them.
- **`Zgłoszenia` is the only admin link in the whole nav.** `/admin/members`, `/admin/classes` and
  `/admin/class-types` are reachable only by typing the URL or through cross-links inside other
  admin screens (`features/admin/classes/classes.html:29`,
  `features/admin/classes/class-form.html:15,20`). Adding a top-menu entry for the exercise library
  would therefore have been a *deliberate departure* from a pattern every prior slice held
  (`context/archive/2026-09-02-class-type-definitions/plan.md:110-111`) — which is why it was raised
  as a decision, and why it was then **dropped from this change** (2026-09-04). `app.html` is not
  touched; the exercise list is reached at `/admin/exercises` by URL, exactly as
  `/admin/class-types` is today. When the entry is eventually added (S-12 or a dedicated nav
  change), its condition must be `isAdmin() && isActive()` — matching `adminGuard` — for the reason
  the comment at lines 8-15 records.

**Auth state** — `core/auth/auth.service.ts:19-36`: `signal<CurrentUser|null>` plus
`isAuthenticated`, `isActive`, `isAdmin` computed signals and a `sessionResolved` flag; role names
mirror the server in `core/auth/roles.ts:13-22`. Cookie session, relative `/api/...` URLs, **no
environment file anywhere** (the SPA is served from the API's own origin).

**List screen** — `features/admin/class-types/{class-types.ts,.html,.scss}`

- Standalone components, always split into `.ts` / `.html` / `.scss`; no inline templates anywhere.
- State is **signals only**; no RxJS in components. Data comes from `HttpClient` via
  `firstValueFrom(...)` in the service. `resource()` / `httpResource` are not used anywhere in the
  repo.
- Four distinct states modelled explicitly in the template (`class-types.html:23-38`): loading
  (`role="status"`), failure (`role="alert"` + retry button), empty, and empty-because-filtered.
- Per-row async actions use a `busy: ReadonlySet<string>` signal plus a `generation` counter to drop
  stale responses (`class-types.ts:57,64,76-89`); the same idiom repeats in `members.ts:68`.
- Layout is a `<ul>` of `<li class="card ...">` — a card list, never an HTML `<table>`. There are no
  `@media` queries in the feature SCSS; layout is flex + `gap: var(--space-N)` on global classes
  (`.card`, `.button`, `.alert`, `.notice`, `.field-error`, `.hint`, `.badge`) from
  `src/app/src/styles.scss`.
- Navigation to edit is a plain `routerLink` (`class-types.html:3,58-60`).

**Create/edit form** — `features/admin/class-types/class-type-form.ts`

- One component serves create and edit, keyed on an optional `:id` (`editingId` signal).
- Reactive forms built with `inject(FormBuilder).nonNullable.group({...})` (lines 57-69); validators
  mirror the server bounds via named constants with a "keep the two in step" comment.
- Per-field errors: `@if (control.touched && control.invalid)` + `hasError('key')` branches, plus
  `[attr.aria-invalid]` (`class-type-form.html:29-39`).
- Submit (lines 107-142): `markAllAsTouched()` on invalid, build the request from
  `getRawValue()`, then `router.navigate(['/admin/class-types'])` on success.
- Server failures are routed to the **responsible control**, not to a banner: `applyFailure()`
  (148-174) switches on the `reason` string and calls `control.setErrors(...)` +
  `markAsTouched()`; only reasons with no owning control fall back to a form-level `error` signal.
  Reviewers have flagged violations of this rule twice
  (`class-type-definitions` F6, `occurrences-from-class-types` F3).
- `shared/` holds only `calendar/` and `week-navigator/` — there are **no reusable form/UI
  components**; forms are hand-built on global CSS classes.

**Services and DTOs** — `core/scheduling/class-type.service.ts` +
`core/scheduling/class-type.models.ts`

- `@Injectable({providedIn:'root'})`, `inject(HttpClient)`, one `Promise`-returning method per
  operation via `firstValueFrom`, `encodeURIComponent(id)` in every path parameter.
- Interfaces named `<Feature>Summary` / `<Feature>Request` / `<Feature>Failure`, each carrying a doc
  comment "Mirrors the API's X record (src/Application/....cs). Keep the two in step — this is a
  contract, not a convenience type."
- When a failure catalogue is shared by several screens it gets its own file with a
  `Record<Reason,string>` map plus a lookup function (`core/scheduling/class-failure.ts`); a
  single-form feature keeps the switch inline.

**Tests and tooling** — Vitest through `@angular/build:unit-test`, jsdom. From `src/app/`:
`npm test`, `npm run quality:check` (`prettier --check . && ng lint`). Every admin feature has a
co-located spec (`class-types.spec.ts`, `class-type-form.spec.ts`, `members.spec.ts`,
`admin.guard.spec.ts`). Pattern: `TestBed.configureTestingModule({ imports:[Component],
providers:[provideHttpClient(), provideHttpClientTesting(), provideRouter([])] })`, then
`vi.waitFor(() => controller.expectOne(url)).flush(data)`, a `settle()` helper, DOM assertions via
`querySelectorAll`, `afterEach(() => controller.verify())`.

### 3. Evidence for the four open decisions

#### (a) Muscle group — enum-as-int vs lookup table

Five enums exist, all in `Domain`, all persisted as **int** via `HasConversion<int>()`, all with an
XML doc warning that the numbers must not be reordered:

| Enum | File | EF configuration |
| --- | --- | --- |
| `AccountStatus` | `src/Domain/AccountStatus.cs:12-22` | `ApplicationUserConfiguration.cs:21-24` (`.HasDefaultValue(Pending)`) |
| `ClassStatus` | `src/Domain/Scheduling/ClassStatus.cs:18-25` | `ClassConfiguration.cs:42-45` |
| `BookingStatus` | `src/Domain/Scheduling/BookingStatus.cs:12-23` | `BookingConfiguration.cs:22-25` |
| `NotificationChannel` | `src/Domain/Notifications/NotificationChannel.cs:11-15` | `OutboxMessageConfiguration.cs:16` |
| `OutboxStatus` | `src/Domain/Notifications/OutboxStatus.cs:10-25` | `OutboxMessageConfiguration.cs:17` |

`HasConversion<string>()` is used nowhere. Over the wire, enums that the UI must render cross as the
**enum name string** — `MemberSummary.Status` is a `string` with a doc explaining that "a badge keyed
on 2 would break the day someone renumbers"
(`src/Application/Members/MemberAdminEndpoints.cs:22-27`), and `ClassEndpoints.cs:985` sends
`entity.Status.ToString()`. Angular types them as **string-literal unions**, not TS enums:
`type MemberStatus = 'Pending' | 'Active' | 'Blocked'` (`core/admin/member-admin.models.ts:23`).
Filtering by enum is done by binding the query parameter as `AccountStatus?` and letting the model
binder reject bad values with a 400 (`MemberAdminEndpoints.cs:139-143`).

The single lookup-table precedent is `ClassType` itself — used precisely because the set is
**admin-extensible**. So the codebase's own line is: fixed, code-defined set → enum-as-int exposed
as a name string; admin-extensible set → its own entity with a filtered unique index and
deactivation. Which side "muscle group" falls on is a product decision, and the plan should state
which one it takes and why (adding a second admin-managed dictionary in the same slice roughly
doubles it).

#### (b) YouTube: URL vs videoId, and where the thumbnail comes from

- **Nothing in `src/` parses a URL.** A repo-wide search for `Uri.`, `UriKind`, `TryCreate` returns
  zero hits. The one URL-shaped column, `PushSubscription.Endpoint`
  (`src/Domain/Notifications/PushSubscription.cs:23`, `HasMaxLength(1000)` with a comment that "FCM
  and Mozilla both run well past 256 characters"), is validated only for
  `IsNullOrWhiteSpace` (`PushEndpoints.cs:56-61`) and passed through raw — it is opaque data the
  browser produced, which is not the case for a video link an admin types.
- **Nothing in the frontend displays an image.** An exhaustive search for `<img`, `ngSrc`,
  `NgOptimizedImage`, `DomSanitizer`, `bypassSecurityTrust`, `thumbnail`, `avatar`, `photo` across
  every `.ts/.html/.scss` under `src/app/src/app/` returns **zero matches**. A thumbnail is a new
  UI primitive, not an existing one — it needs its own success criteria (alt text, a fallback when
  the image 404s, and a fixed aspect box so the card list does not reflow).
- Implication for the plan: deriving `https://img.youtube.com/vi/<id>/mqdefault.jpg` requires an
  extracted `videoId`. Extraction has to happen somewhere, and the house style (validation mirrored
  on both sides, structured `reason` on the responsible control) points to parsing on the server at
  write time and refusing an unrecognised link with a reason like `invalid_video_url`. Whether the
  stored column is the raw URL, the extracted id, or both is the decision to record; whether the
  thumbnail URL is computed in the DTO or in the Angular component is a second, smaller one.
  Both are *new* seams — no precedent constrains them.

#### (c) Delete vs deactivate

Soft state changes dominate:

- `ClassType.IsActive` — `POST /{id}/deactivate` and `/activate`
  (`ClassTypeEndpoints.cs:127-128`, handlers 254-311). `IClassTypeStore` explicitly documents "No
  Remove". Filtering is an explicit `Where` in the caller; **no `HasQueryFilter` exists anywhere in
  `src/Infrastructure/`** — `GetAllAsync` deliberately returns active *and* inactive rows and the
  screen's "pokaż nieaktywne" toggle filters client-side (`ClassTypeEndpoints.cs:134-143`).
- `ApplicationUser.Status` — block/unblock (`MemberAdminEndpoints.cs:104-105`), where blocking
  cascades into cancelling future bookings (`BlockAsync`, 269-290).
- `Booking.Status` and `Class.Status` — cancelling flips a status and keeps the row
  (`src/Domain/Scheduling/Booking.cs:58-62`, "CANCELLING DOES NOT DELETE"); even
  `DELETE /api/classes/{classId}/bookings/mine` (`BookingEndpoints.cs:138`) only flips state.

Exactly one true hard delete exists: `DELETE /api/admin/classes/{id}` (`ClassEndpoints.cs:228`,
handler 649-679) calls `store.Remove(...)`, and it is refused with `409 has_bookings` whenever any
booking ever referenced the class — backed at the DB by `DeleteBehavior.Restrict` on both FKs
(`ClassConfiguration.cs:69-72`, `BookingConfiguration.cs:34-42`). The doc comment
(`ClassEndpoints.cs:157-161`) frames the rule the plan should reuse: *"CANCEL AND DELETE ARE
DIFFERENT ACTIONS… DELETE is for a MISTAKE."*

For exercises the relevant future constraint is S-11: a training plan will reference exercises, so a
hard delete would either orphan plan rows or be blocked by a `Restrict` FK. The `ClassType`
precedent (deactivate + filtered unique index on the name among active rows) transfers directly and
is the low-risk choice; a `Class`-style delete-only-if-never-referenced is the alternative, and it
cannot be fully implemented until `TrainingPlanExercise` exists.

#### (d) Long text, optional fields, and the shape of the form

- Long text: `ClassType.Description` is `HasMaxLength(1000)` with a matching server check
  (`description_too_long`) and a matching `Validators.maxLength(1000)` client-side
  (`class-type-form.ts:30,60`). `OutboxMessage.Body` is the only unbounded column, and only because
  it is machine-rendered (`OutboxMessageConfiguration.cs:24`). An exercise's
  preparation / starting-position / execution instructions are user-typed prose and therefore need
  explicit ceilings on both sides — the review history shows an unbounded field mirrors straight
  into a 500 (`class-type-definitions` F2, `class-change-notifications` F1).
- Optional fields (FR-018 says every descriptive field is optional): nullable CLR type in Domain, no
  `IsRequired()` in the configuration, nullable DTO field, `string | null` in the TS model, and the
  form control kept as a non-null string with explicit conversion at both boundaries —
  `existing.description ?? ''` on load and `description.length === 0 ? null : description` on submit
  (`class-type-form.ts:96,118,123`), with the server normalising blank to null
  (`NormalizeDescription`, `ClassTypeEndpoints.cs:358-359`). "Absent" has exactly one
  representation: `null`.
- Note the shape difference from every prior slice: `ClassType` has four fields and one screen pair.
  An exercise has ~8 optional fields plus a video and needs **three** screens (list, detail, form),
  because the user asked for a read-only detail view with an "edit" action rather than a list that
  links straight into the form. That detail screen is new; nothing in `features/admin/` is a
  read-only detail view today.

### 4. Process conventions this change inherits

- **Plan skeleton** (identical across all 12 archived changes): Overview → Current State Analysis →
  Desired End State (with Key Discoveries) → What We're NOT Doing → Implementation Approach →
  Critical Implementation Details → `## Phase N` sections → Testing Strategy → Performance
  Considerations → Migration Notes → References → Progress. Each phase carries per-file
  **Intent + Contract**, Success Criteria split into `#### Automated Verification` /
  `#### Manual Verification`, and closes with a "pause here for manual confirmation" note.
- **Phase count**: 2–4, most commonly 3. `class-type-definitions` used exactly: *Model and schema* →
  *The admin API* → *The admin screens*.
- **`plan-brief.md` accompanies every `plan.md`** — a condensed What & Why / Starting Point / Desired
  End State / Key Decisions table / Scope / Phases at a Glance / Open Risks / Success Criteria.
- **`## Progress`** convention: `- [ ]` pending, `- [x]` done, append ` — <commit sha>` when a step
  lands; never rename step titles; grouped per phase under `#### Automated` / `#### Manual`.
- **Commits** (from `git log`, not from `context/`): conventional commits scoped by change-id with a
  phase suffix — `feat(class-booking-and-cancel): model, schema and the booking write path (p1)`,
  `fix(<change>): apply implementation review findings`, `chore(archive): close <change>`.
- **Every change ends with `reviews/impl-review.md`**; `plan-review.md` and `research.md` are
  occasional.

### 5. Recurring review findings to pre-empt

Distilled from `reviews/*.md` across all 12 archived changes, ordered by how often they recur:

1. **Plan/implementation drift left unrecorded** — the single most repeated finding
   (`persistence-foundation` F3, `class-schedule-and-admin` F2, `occurrences-from-class-types`
   F4–F6, `schedule-calendar-view` F10, `class-booking-and-cancel` F2, `class-change-notifications`
   F2–F3). This is also the one entry in `context/foundation/lessons.md`: record the adaptation in
   `plan.md` in the same phase, not only in a log.
2. **Manual success criteria left unchecked while the change is stamped complete**
   (`auth-identity-foundation` F7, `class-booking-and-cancel` F5, `class-change-notifications` F9 —
   22 unchecked criteria).
3. **A missing validation bound that mirrors a DB constraint turns into a deterministic 500**
   (`class-type-definitions` F2, `registration-and-approval` F7/F9,
   `class-change-notifications` F1).
4. **Races surfacing as 500s instead of clean 409s** (`class-type-definitions` F3,
   `class-schedule-and-admin` F3, `class-booking-and-cancel` F1). The house response is either
   catching the unique-index violation, or explicitly recording the single-admin acceptance with "a
   second admin as the trigger to revisit".
5. **Comments that no longer match the code** (`member-management` F3,
   `trainer-role-and-assignment` F3/F6, `class-change-notifications` F5) — a real risk here given how
   comment-heavy this codebase is.
6. **A server refusal shown as a banner instead of on the responsible control**
   (`class-type-definitions` F6, `occurrences-from-class-types` F3).
7. **No test for the load-bearing invariant** (`class-type-definitions` F1 was CRITICAL: the slice
   shipped with zero server tests for the uniqueness rule it existed to enforce).
8. **UI feedback gaps** — a stale notice not cleared on retry, no confirmation on a state-changing
   action, a lost retry control (`class-type-definitions` F7/F8, `schedule-calendar-view` F2).
9. **Performance**: full scans, N+1 lookups, fetching a whole collection to edit one row
   (`notification-delivery-foundation` F4/F5, `class-change-notifications` F8,
   `class-schedule-and-admin` F4).
10. **Migration re-apply hazards** — an unconditional `DELETE` in `Up` re-fires on Down→Up
    (`class-type-definitions` F4).

## Code References

- `src/Domain/Scheduling/ClassType.cs:28-66` — the entity shape to copy (POCO, nullable optional, `IsActive`)
- `src/Infrastructure/Persistence/Configurations/ClassTypeConfiguration.cs:7-41` — lengths + filtered unique index
- `src/Infrastructure/Persistence/AppDbContext.cs:27` — `DbSet` registration convention
- `src/Infrastructure/Persistence/Migrations/20260902111715_AddClassTypes.cs:12-101` — `Up`/`Down` shape
- `src/Application/Scheduling/ClassTypeEndpoints.cs:23-58` — DTO records; `:113-131` group + routes; `:318-351` validation; `:375-404` store/query seams
- `src/Application/Members/MemberAdminEndpoints.cs:22-27,139-143` — enum over the wire as a name string; enum-bound query filter
- `src/Application/Scheduling/ClassEndpoints.cs:157-161,649-679` — the only hard delete, and the rule that guards it
- `src/Domain/AuthorizationPolicyNames.cs` — `Admin` / `ActiveMember` policy names in Domain
- `src/Infrastructure/Scheduling/ClassTypeQuery.cs`, `ClassTypeStore.cs` — read/write seam implementations
- `Program.cs:199-200,247-253` — DI registration and endpoint mapping
- `tests/po-prostu-silka.Tests/ClassTypeEndpointTests.cs:17-40` — unique-data-per-test rule; `EveryRoute` / `InvalidRequests` theories
- `tests/po-prostu-silka.Tests/IntegrationTestFixture.cs:25-158` — Testcontainers + seeded users + authenticated client
- `src/app/src/app/app.routes.ts:23-60` — route + guard shape, `new` before `:id`
- `src/app/src/app/app.html:6-34` — the entire top nav; the `isAdmin() && isActive()` condition
- `src/app/src/app/core/auth/auth.service.ts:19-36` — role/status signals
- `src/app/src/app/features/admin/class-types/class-types.ts:57,64,76-89` — `busy` set + `generation` guard
- `src/app/src/app/features/admin/class-types/class-type-form.ts:57-69,96,107-174` — form build, null↔'' conversion, `applyFailure`
- `src/app/src/app/core/scheduling/class-type.models.ts:10,40` — DTO mirror convention
- `src/app/src/app/features/admin/class-types/class-types.spec.ts:1-100` — Vitest + `HttpTestingController` pattern

## Architecture Insights

- **The layering rule holds today.** `Application` never references EF Core; every data access goes
  through an intention-revealing `IXQuery` / `IXStore` pair declared beside the endpoints and
  implemented in `Infrastructure`, sharing the request-scoped `AppDbContext` with `UnitOfWork`. A
  new `IExerciseQuery` / `IExerciseStore` pair is the expected shape; introducing a generic
  repository would be the deviation.
- **Invariants live at the edges, not in the entity.** Validation is duplicated deliberately in
  three places (endpoint `Validate()`, EF column, Angular validator) with "keep the two in step"
  comments as the only glue. That duplication is the convention *and* the source of the most
  frequent review finding — the plan should list the bounds once, as a table, and reference it from
  all three places.
- **Errors are structured data, not prose.** `{ "reason": "snake_case" }` with `400` for validation
  and `409` for conflict; the client maps each reason to the control it belongs to. No
  ProblemDetails anywhere.
- **Soft state beats deletion nearly everywhere**, and the one exception is fenced by both an
  application guard and a `Restrict` FK.
- **The frontend is signals-first, card-based, and mobile-first without media queries**, leaning on
  a global class vocabulary in `styles.scss`. There is no shared component library to extend — new
  UI is built from those global classes.
- **Three admin screens is a new shape.** Existing admin features are list + form. A read-only
  detail screen with an edit action does not exist yet, so its states (loading / not-found /
  loaded), its back-link, and its relationship to the form route are all new decisions.

## Historical Context (from prior changes)

- `context/archive/2026-09-02-class-type-definitions/plan.md` — the closest precedent end-to-end;
  phases *Model and schema* → *The admin API* → *The admin screens*; `:105` "no `DELETE` endpoint";
  `:313` `IsActive` excluded from the request DTO "so a careless edit cannot silently resurrect a
  deactivated type"; `:110-111` "Not adding a global nav entry. Consistent with `/admin/members` and
  `/admin/classes`".
- `context/archive/2026-09-02-class-type-definitions/plan-brief.md` — the decisions-table format the
  next plan brief should follow.
- `context/archive/2026-09-02-class-type-definitions/reviews/impl-review.md` — REJECTED on first
  pass; F1 (no server tests for the core invariant) and F2 (unvalidated over-long name → 500) are
  the two traps most likely to repeat here.
- `context/archive/2026-09-01-registration-and-approval/reviews/impl-review.md:125-133` — the nav/guard
  mismatch bug that fixes the exact condition a new menu entry must use.
- `context/archive/2026-09-01-member-management/frame.md:46` — confirms no `Exercise` or
  `TrainingPlan` type has ever existed in `src/`.
- `context/archive/2026-09-03-class-booking-and-cancel/plan.md:618-628` — the only precedent for
  *adding* nav links, with the `routerLinkActive="is-active"` + `isActive()` gating pattern.
- `context/foundation/roadmap.md:244-255` — S-10's stated outcome (description, muscle group,
  difficulty, equipment, preparation / starting-position / execution instructions, all optional, plus
  an instructional video), its `S-01` prerequisite, and the open content-entry question.
- `context/foundation/prd.md:110-115,163` — FR-018, FR-019, FR-020 and the "no standalone exercise
  library browsing" non-goal (member-facing; admin management is FR-018).
- `context/foundation/lessons.md` — the one recorded lesson: write adaptations back into the plan
  within the same phase.

## Related Research

- `context/archive/2026-09-03-class-booking-and-cancel/research.md` — the only other `research.md` in
  the repo; useful as a format reference and for its nav findings (`:252-253`).

## Open Questions

1. **Muscle group: enum or dictionary entity?** The codebase supports both idioms cleanly. An enum
   ships in one migration and cannot be extended without a deploy; a dictionary entity doubles the
   slice and pulls in its own list screen. (Decision for `/10x-plan`.)
2. **Does an exercise support more than one muscle group?** The user's list screen shows "grupa
   mięśni" singular; S-11 filtering may want many. A `many` shape is materially harder to reverse
   later than a `one` shape.
3. **YouTube storage: raw URL, extracted `videoId`, or both** — and where the thumbnail URL is
   composed (server DTO vs Angular component). Related: what happens for an unrecognised link
   (refuse at write with a `reason`, or accept and show no thumbnail).
4. **Deactivate or delete?** `ClassType`-style deactivation transfers directly and pre-protects
   S-11's plan references; a `Class`-style guarded delete cannot be completed before
   `TrainingPlanExercise` exists.
5. ~~**How wide does the nav change go?**~~ **Resolved 2026-09-04:** no nav change at all. The
   exercise library stays URL-reachable like every other admin screen; the whole question of how
   admin entries are surfaced moves to S-12. Whether the exercise list gets a cross-link from
   another admin screen (the way `/admin/classes` links to `/admin/class-types`) is a small,
   separate call for the plan — there is no obvious screen to hang it off, since the training
   context has no other screens yet.
6. **"ekran listy zajec" in the change notes** almost certainly means the exercise list, not a
   class list; worth confirming before the plan freezes the wording.
7. **Content entry (roadmap S-10 unknown, PRD Open Question 2)** — who types the initial exercises,
   and does this slice need a seed or an import path? Not a build blocker.
