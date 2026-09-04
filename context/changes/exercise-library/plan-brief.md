# Exercise Library — Plan Brief

> Full plan: `context/changes/exercise-library/plan.md`
> Research: `context/changes/exercise-library/research.md`

## What & Why

Training plans (S-11) cannot exist until there is something to put in them. This slice builds the
admin-facing exercise library: an exercise defined once with a name, seven optional descriptive
fields and an instructional YouTube video, then browsed, read, edited and deactivated. It is roadmap
item **S-10**, delivering `prd.md` FR-018 and FR-019, and it is the head of the training bounded
context — buildable independently of everything in scheduling.

## Starting Point

The training context does not exist: no `Exercise`, no `TrainingPlan`, no `src/Domain/Training/`.
What does exist is a complete, idiomatic admin-CRUD vertical to copy — `ClassType` (S-05) — from the
entity and its filtered unique index through a minimal-API group under the `Admin` policy, the
`IXQuery`/`IXStore` seams that keep EF Core out of `Application`, an Angular list + form pair, and
integration tests on a real SQL Server. Two things in this slice have no precedent anywhere in the
repo: displaying an image (there is not a single `<img>` in the frontend) and parsing a URL
(`Uri.TryCreate` appears nowhere in `src/`).

## Desired End State

An admin opens `/admin/exercises`, sees a card list with a muscle-group badge and a YouTube
thumbnail per row, and adds an exercise by pasting a link in whatever shape YouTube gave them.
Clicking a row opens a readable detail page — every populated field, the video playing inline, an
"Edytuj" button. Names cannot collide among active exercises, an exercise is deactivated rather than
deleted, and a deactivated name becomes free again.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Nav entry | None — URL-reachable only | Every prior admin slice deliberately added none, and surfacing admin entries belongs to S-12's dashboards. | Plan (user, 2026-09-04) |
| Muscle group | Free text (100 chars) + `<datalist>` suggestions from existing values | Nothing reads it programmatically yet, so an enum buys strictness nobody pays for; the suggestions remove name drift in practice without a second entity. | Plan |
| Difficulty | Same treatment as muscle group | Same reasoning, and one mechanism is cheaper to maintain than two. | Plan |
| YouTube link | Parsed server-side; only the 11-character `videoId` is stored | A bad link becomes a `400` on the field at write time instead of a broken image discovered later, and both thumbnail and player get one trustworthy source. | Plan |
| Video URL round-trip | Edit form shows a canonical `watch?v=<id>` rebuilt from the id | Storing one representation is worth the admin not seeing back the exact string they pasted. | Plan |
| Player | Embedded `youtube-nocookie.com/embed/<id>` iframe on the detail screen | Watching a technique without leaving the app is what S-11 will need anyway; no CSP exists to amend and the service worker ignores cross-origin requests. | Plan |
| Lifecycle | Deactivate/activate, no `DELETE` | Matches the dominant repo pattern and pre-protects the plan references S-11 will add. | Research |
| Name uniqueness | Filtered unique index among active rows, `409 name_taken` | Copies `ClassType` exactly, including the reactivation-collision re-check that its review found. | Research |
| Fields | Full FR-018 set in this slice | Closes the requirement in one migration so S-11 is not waiting on a second one. | Plan |
| Screens | Four routes: list, `new`, `:id` (read-only), `:id/edit` | A read-only page is what makes eight prose fields readable; reading them as form values is what the `class-types` shape would force. | Plan |

## Scope

**In scope:** the `Exercise` entity and its EF configuration; a pure YouTube-id parser in `Domain`
with unit tests; one additive, reversible migration; the `/api/admin/exercises` group (list, detail,
create, update, deactivate, activate) with validation and both persistence seams; integration tests
covering authorization, bounds, uniqueness and the absence of a delete route; the Angular service,
models, list, form and detail screens with their routes and Vitest specs.

**Out of scope:** any top-menu or nav change; any member-facing surface; training plans and
assignment (S-11); hard delete; a muscle-group or difficulty dictionary; image upload or self-hosted
video; search, filtering, sorting or pagination beyond the active/inactive toggle; CSP work; seed
data; a concurrency token.

## Architecture / Approach

The `ClassType` vertical, copied into a new training context and changed only where this slice
genuinely adds something:

```
Domain/Training/Exercise.cs + YouTubeVideoId.cs        (pure parser, unit-tested)
  └─ Infrastructure/Persistence/Configurations/ExerciseConfiguration.cs   (filtered unique index)
Application/Training/ExerciseEndpoints.cs              DTOs + IExerciseQuery / IExerciseStore
  └─ Infrastructure/Training/ExerciseQuery.cs, ExerciseStore.cs
app/core/training/exercise.models.ts + exercise.service.ts + youtube.ts
  └─ features/admin/exercises/{exercises, exercise-form, exercise-detail}
```

The request carries `videoUrl` and the response carries `videoId`: the server owns parsing, the
client owns composing thumbnail and embed URLs from the id. `GET /` returns active and inactive rows
in one unfiltered call — the list toggles client-side, and the form reuses the same call to derive
its suggestions, so no extra endpoint exists.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Model and schema | Entity, video-id parser + its unit tests, EF configuration, `DbSet`, additive migration | The parser's accepted shapes are the contract everything downstream trusts — a gap here surfaces as a broken thumbnail much later |
| 2. The admin API | Six endpoints under the `Admin` policy, ten failure reasons, both seams, integration tests | A missing length bound mirrors straight into a deterministic 500 — the exact CRITICAL finding from the `class-type-definitions` review |
| 3. The list and the form | Service, models, list with thumbnails and the inactive toggle, create/edit form with suggestions, routes, specs | Failure-to-control mapping must cover all ten reasons, or a refusal lands in the banner the plan rules out |
| 4. The detail screen | Read-only page with every field and an embedded player, "Edytuj", `404` state | First `bypassSecurityTrustResourceUrl` in this frontend — must be computed per id, not in a template expression |

**Prerequisites:** S-01 closed (it is). Docker SQL Server running locally for the migration and the
Testcontainers suite.
**Estimated effort:** ~3-4 sessions, roughly one per phase.

## Open Risks & Assumptions

- Free-text muscle groups will drift if the suggestions are ignored; the datalist is a nudge, not a
  constraint. If S-11 or a dashboard ever wants to group by muscle group, the cleanup is manual
  mapping over free text — cheaper now than after dozens of exercises exist.
- YouTube link shapes change over time; storing only the id means a shape the parser misses is a
  refusal the admin sees immediately, which is the failure mode worth having.
- The embedded player is the first third-party frame in the app. No CSP exists today, so nothing
  blocks it — but whoever adds a CSP later must allow `frame-src youtube-nocookie.com` and
  `img-src img.youtube.com`, or this screen breaks silently.
- No concurrency token, consistent with the rest of the admin surface; two admins editing the same
  exercise is last-write-wins.
- Content entry remains open (`roadmap.md:253`) — the library ships empty and stays useless until
  someone types dozens of exercises.

## Success Criteria (Summary)

- An admin can define an exercise once, with as much or as little detail as they have, and find,
  read, edit and deactivate it without ever hard-deleting anything.
- A YouTube link pasted in any common shape produces a working thumbnail on the list and a playing
  video on the detail screen; a link that is not YouTube is refused on the field that caused it.
- Two active exercises cannot share a name, and nothing in scheduling, membership or notifications
  changes behaviour.
