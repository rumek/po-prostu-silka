# Repository Guidelines

"Po Prostu Siłka" is a gym class-booking and training-plans web app: an ASP.NET Core Web API (`net10.0`, C#) and an Angular 22 SPA with SSR as sibling projects in one repo. Product requirements live in @context/foundation/prd.md; stack rationale in @context/foundation/tech-stack.md.

## Hard rules

- Never write to `context/archive/` — archived changes are immutable. If a target path resolves there, stop and open a new change instead.
- No overbooking: any booking logic must guarantee a class never accepts more bookings than it has spots (SQL Server transaction via EF Core, per the PRD guardrail).
- MVP notifications are email + push only; do not add an in-app notification center — that scope was explicitly rejected in the PRD.
- **Booking is a staff action, and the karnet is what gates training** (S-16, supersedes v1 US-01/FR-002/FR-003/FR-008/FR-009). A member never books or cancels their own spot: an admin books anyone, a trainer books into the classes they personally instruct, and nobody may be booked without a `MembershipPass` valid on the class's club-local date with a free entry. Entries left is DERIVED from active bookings carrying that pass's id — never a stored counter. Admin approval of new accounts is gone; registration produces an active account and is rate-limited instead. `AccountStatus.Pending` stays declared but is never produced.
- Known accepted risk: transitive HIGH vulnerability in `Microsoft.OpenApi 2.0.0` (GHSA-v5pm-xwqc-g5wc); don't "fix" it by downgrading `Microsoft.AspNetCore.OpenApi` — pin a patched transitive reference when available.

## Project structure

- `src/` — the .NET backend as **four projects**: `Domain/`, `Application/`, `Infrastructure/` and `Api/` (the host, `Program.cs`). Shared MSBuild properties live in `src/Directory.Build.props`.
- `src/app/` — the full Angular workspace (its own `package.json`, `angular.json`); Angular source is at `src/app/src/app/`. Don't confuse the two `src` levels.
- `context/` — foundation docs and change logs (see @context/foundation/README.md).

### Layering (enforced by the compiler)

Four projects, one per layer. Bounded contexts (membership, scheduling, training, notifications) become subfolders within them as their slices land.

| Project | May reference |
| --- | --- |
| `src/Domain/` | nothing but the BCL and one Identity package (see below) |
| `src/Application/` | `Domain` |
| `src/Infrastructure/` | `Domain`, `Application` — and it is the **only** project that may reference EF Core |
| `src/Api/` | `Application`, `Infrastructure` — the host, and the `dotnet ef` startup project |

- EF Core artifacts (`AppDbContext`, entity configurations, migrations) live under `src/Infrastructure/Persistence/`. Never put a `using Microsoft.EntityFrameworkCore` in `Domain` or `Application`.
- Entity configuration goes in `IEntityTypeConfiguration<T>` classes under `Infrastructure/Persistence/Configurations/` — they are auto-discovered by `ApplyConfigurationsFromAssembly`, so don't accumulate fluent config in `OnModelCreating`.
- **This is now a build constraint, not a convention.** Adding `using Microsoft.EntityFrameworkCore;` to a file in `Domain` or `Application` fails `dotnet build` with CS0234. The escalation that used to be "split into separate projects if it rots" has been taken (S-18).
- **"Domain references nothing" is no longer literally true**, and the exception is deliberate: `Domain` carries `Microsoft.Extensions.Identity.Stores`, because `ApplicationUser : IdentityUser` and `IdentityUser` lives there. `IdentityUser` is Identity, **not** EF Core, and the rule the build enforces names EF Core specifically. Do not "fix" this by moving `ApplicationUser` to `Infrastructure`.
- The one remaining hole is a package: adding an EF-Core-bearing `PackageReference` to `Application.csproj` would compile. That is a visible, reviewable csproj diff — there is deliberately no architecture test.

### Database

- Local dev runs SQL Server in Docker: `docker compose up -d` (root `docker-compose.yml`), connection string in `src/Api/appsettings.Development.json`. A real engine, not SQLite — locking semantics must match Azure SQL for the no-overbooking guarantee.
- Production is Azure SQL (Basic DTU). The connection string comes from the App Service connection string named `Default`, type `SQLAzure` — both exact values matter, since the platform maps `SQLAZURECONNSTR_Default` back onto `ConnectionStrings:Default`.
- `GET /health` opens a real DB connection; use it to check connectivity rather than inferring it.
- Migrations must be reversible (working `Down`). Rollback redeploys the previous artifact but does **not** roll back schema, so destructive changes lag one release behind the code that stops needing them.
- `nuget.config` pins nuget.org only. Keep the `<clear />` — this machine has private feeds that CI cannot reach.

## Build, test, and dev commands

Backend, from the repo root: `dotnet build po-prostu-silka.slnx`, `dotnet run --project src/Api/po-prostu-silka.Api.csproj`, `dotnet list package --vulnerable` (the audit used at bootstrap). `dotnet ef` needs both projects: `--project src/Infrastructure/po-prostu-silka.Infrastructure.csproj --startup-project src/Api/po-prostu-silka.Api.csproj`. Tests live in `tests/po-prostu-silka.Tests/` — run them from the repo root with `dotnet test`. They are integration tests: `IntegrationTestFixture` boots the real app via `WebApplicationFactory<Program>` against a real SQL Server started by Testcontainers, so behaviour that depends on the engine (filtered unique indexes, locking) is actually exercised. CI gates the deploy on `dotnet test`.

Frontend, from `src/app/` (npm 11, pinned via `packageManager`): `npm start` (dev server), `npm test` (unit tests via Vitest), `npm run quality:check` / `quality:fix` (Prettier + ESLint — run `quality:check` before committing frontend changes).

- **Node 22+ is required** — the Angular CLI refuses to start below it. If `npm` commands fail with a version complaint, the shell is on an older default; select a newer Node for the command rather than switching the machine's global version.
- **The initial-bundle warning in `angular.json` is 600 kB** (error at 1 MB) — raised from 550 kB in S-23, and from 500 kB in S-12 before that. The S-12 figure was an estimate rather than a measured constraint, and the dashboard at `/` is deliberately eager — a lazy landing route would put a round trip between signing in and seeing anything, for every member, every visit. Keep routes lazy by default anyway: everything except `login`, `register`, `pending` and `/` is, and that is what has kept the eager bundle viable. The S-23 raise came before the kit's components landed in the eager screens rather than after measuring them — a deliberate call, and one that repeats the 500→550 pattern this paragraph warns about: at 86 kB of slack over the last measurement the budget warns about nothing until a large regression. **Measured at 513.90 kB after S-23**, from 512.42 kB after S-19 and 509.68 kB at that slice's midpoint — the whole six-component kit cost 1.48 kB. At S-19: the toast host and `@angular/cdk/a11y`'s `LiveAnnouncer` are the first CDK code in the eager chunk and cost roughly 3 kB between them — `cdk/overlay` was declined partly for this reason. The number is recorded because it was measured, not because it became a problem.

## Style

- C#: nullable reference types and implicit usings are enabled — keep new code warning-free under `<Nullable>enable</Nullable>`.
- Angular: formatting/linting is enforced by Prettier and angular-eslint (@src/app/eslint.config.js), not by hand.

### How a failure reaches the user (S-19)

Every failure in the SPA takes **one of four outlets**, and which one is not a taste call. Before
S-19 there were nine display mechanisms and ten separately worded generic fallbacks; the point of
this rule is that a new screen makes exactly one decision instead of inventing a tenth mechanism.

The **words** always come from a table, never from the screen: `core/http/failure-messages.ts`
builds every `*FailureMessage` table, one per `*Failure` union, and a union without a table fails
`core/http/failure-contract.spec.ts`. A screen decides the outlet; it never writes the sentence.

1. **Field error** — the refusal names a control the user can correct on the form in front of
   them. The words come from the union's table; the form decides only *which control*. Set it with
   `state.reject(control, errors)` from `shared/forms/form-state.ts`.
2. **Form banner** — `.alert` with `role="alert"`, inside the form — the refusal concerns the
   submission as a whole, names no single control, or naming one would disclose something.
   **Login is permanently in this bucket and never in the first**: a field-level "no such account"
   tells an attacker which e-mails exist. `login.spec.ts` pins this.
3. **Toast** — `shared/toast/toast.service.ts` — the action finished somewhere that is not a form
   and the screen stays put: row actions, list activations, overlay actions, clipboard. It carries
   `success` and `info` as well as `error`; `info` is what a recovered conflict ("lista była
   nieaktualna — odświeżono") becomes, which is neither. Errors persist until dismissed;
   `success` and `info` time out.
4. **Screen state** — the screen could not be populated at all (`loadFailed`, `notFound`).
   **Never a toast**: there would be nothing behind it to read.

Transport failures — a 401/403, a 404, a 429, a 5xx, an offline browser — route by the same four
outlets, but their words come from `core/http/transport-messages.ts` rather than from a union
table. `classifyFailure` in `core/http/failure.ts` is what tells them apart; nothing unwraps
`.error.reason` by hand. A 409 is **not** a transport kind — it always carries a `reason`, so it
is a business refusal like any other.

`z-index` values for surfaces that resolve at the **document root** — the skip link, the bottom
nav, the overlays, the toast — come from the `--z-*` scale in `src/styles.scss`; never write a
literal for one of those. Neither `App`'s `:host` nor `.shell-main` opens a stacking context, so
those four surfaces all stack against each other and the four numbers only work as a set.
A `z-index` that is local to its own positioned ancestor is outside the scale and stays a literal
— `schedule-calendar.scss` is the one such case, where absolutely-positioned overlays stack
within a single calendar tile.

### The presentational kit (S-23)

S-19 unified how a failure is *told*; it left the presentational layer alone. Six families of
copied markup now exist once each, and a seventh hand-rolled copy **fails the build** rather than
merely reviewing badly.

| Instead of | Write | Notes |
| --- | --- | --- |
| `<div class="field">` | `<app-field label="…" for="…">` | Both inputs optional; a field whose label is an element projects into `[slot=label]` instead. |
| a bare `<select>` | `<app-select><select …></app-select>` | Wrapper only. Five of seven selects shipped without it and therefore had **no arrow at all**. |
| `<input type="checkbox">` | `<app-checkbox [checked] (checkedChange)>` | No `ControlValueAccessor` — both callers are filter toggles. Additive when a form needs one. |
| `Wczytywanie…` | `<app-loading />` | **No input.** The word lives in the component so the app cannot end up with two of it. |
| `<p class="empty">` | `<app-empty>` | Projects, unlike `app-loading`: no two empty states say the same thing and two carry links. |
| `<ul class="x">` / `<li class="card x-row">` | `<app-list>` / `<li appRow>` | `.row-identity`, `.row-name`, `.row-meta`, `.row-actions` are global; `card` stays the caller's. |

Three things about this are not taste:

- **Every component projects rather than owns.** That is what let `app-field` absorb all 56 copies,
  including `plan-builder`'s branching label and `profile`'s `server`/fallback pair. The price is
  that no component can check what was projected into it — which is why the lint rule, not the
  components, is the anchor.
- **`li[appRow]` is an ATTRIBUTE selector**, because `<app-row>` as an element would produce
  `ul > app-row`, and a `<ul>` admits nothing but `<li>`. `eslint.config.js`'s `component-selector`
  carries a second entry for it.
- **The row classes live in `src/styles.scss`, not in the components' stylesheets.** Emulated
  encapsulation scopes a component's styles to its own template, and everything in a row arrives by
  projection carrying the *caller's* scope. Same reason `.page-header` and `.link-button` are
  global.

**Enforcement:** `tools/eslint-rules/no-hand-rolled-presentational.js`, wired in `eslint.config.js`
and scoped to **`src/app/features/**`**. The boundary is structural — the kit lives in `shared/`,
screens live in `features/` — so there is no exemption list to maintain. `app-select`'s own
template contains a `<select>` and would fail its own rule under any wider scope. It is an AST rule
rather than a source scan because two of its checks cannot be done on text: a `<select>` is legal
exactly when it has an `app-select` **ancestor**, and `plan-builder.html` contains the literal
string `<select>` inside a comment explaining why that branch renders static text.
`tools/eslint-rules/no-hand-rolled-presentational.spec.ts` carries the negative cases, which are
the point — a rule that over-fires teaches the next contributor to reach for a disable comment.

**What it cannot see:** the rule reads static markup. A class bound through `[class]` or `[ngClass]`, or a loading word built by interpolation, passes lint — it guards against the accidental copy, not a deliberate one, and those are a review finding instead.

**The cost, recorded rather than hidden:** `shared/` was migrated in S-23 too but sits outside the
rule, so nothing stops those templates drifting back.

**`.notice` now carries one meaning** — informational screen content — in five places, instead of
four meanings in thirty-three. Loading is `app-loading`, empty is `app-empty`, success is a toast.

### Breakpoints (S-20)

Every width threshold a stylesheet uses is a mixin in **`src/app/src/styles/_breakpoints.scss`**,
reached with `@use 'breakpoints' as bp;` (`src/styles` is on `stylePreprocessorOptions.includePaths`
in `angular.json`). The values: `narrow` / `above-narrow` (30rem — the phone layout, and an exact
partition of the axis: the header nav and the bottom bar must never both render), `form-columns`
(40rem), and `desk` / `below-desk` (64rem — where a tool built for a mouse, the admin's class
calendar, renders at all). Sass values and not custom properties, because a custom property is
illegal in a media condition.

TypeScript gets only what it needs to **withhold rendering** rather than restyle:
`core/layout/breakpoints.ts` carries `DESK_MIN_WIDTH` / `DESK_MEDIA_QUERY` (the twin of
`$desk-min-width`) and `FINE_POINTER_MEDIA_QUERY`, and `core/layout/media-query.ts`'s
`mediaQuerySignal(query, fallback)` is the one guarded `matchMedia` read — call it in an injection
context, and choose the fallback for a server or jsdom deliberately. A layout-only difference stays in
the stylesheet; `bottom-nav` hides in CSS for exactly that reason.

`core/layout/breakpoints.spec.ts` fails when the Sass and TS desk values disagree, and when any
stylesheet other than the partial writes a literal `min-width` / `max-width` media query.

**48rem is not in the partial.** It is the calendar's day/week switch — how many day columns stay
legible — lives in `shared/calendar/calendar-breakpoint.ts` alone, and must not be merged with the
desk boundary: two different questions, one number, and they drift apart under the first redesign.

### Shared shapes, not copied ones (S-19)

Six blocks were copied from screen to screen until S-19; each now exists once, and a seventh copy
is a review finding rather than a style preference:

- `shared/forms/form-state.ts` — the `loading` / `loadFailed` / `submitting` / `error` signals plus
  `reject(control, errors)`. A FUNCTION held in a field, never a base class: this repo uses no
  component inheritance and every shared component is standalone.
- `shared/forms/busy-set.ts` — `setBusy` / `isBusy` per row, so one slow row does not disable a list.
- `shared/forms/load-fence.ts` — the generation counter. One fence per INDEPENDENT load: the
  dashboard's four cards hold four, and must keep holding four.
- `shared/forms/overlay-focus.ts` — focus into the panel on open, back to the opener on close.
- `.page-header` / `.page-header--detail` in `src/styles.scss` — the eight page headers.
- `.overlay-backdrop` / `.overlay-panel` / `.overlay-title` / `.overlay-when` / `.overlay-actions`
  and `.link-button`, also global. The overlay's fixed layer stays each component's own `:host`,
  because a global class cannot reach it.

## Commits & CI

History has no established commit convention yet — short imperative subjects until one is defined. CI is `.github/workflows/deploy.yml`: it runs the SPA specs and `dotnet test po-prostu-silka.slnx`, then publishes `src/Api/po-prostu-silka.Api.csproj` to Azure App Service and applies migrations with `dotnet ef`, on merge to `main`.
