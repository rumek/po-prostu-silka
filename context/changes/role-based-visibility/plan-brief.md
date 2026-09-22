# Role-based Visibility — Plan Brief

> Full plan: `context/changes/role-based-visibility/plan.md`
> Research: `context/changes/role-based-visibility/research.md`

## What & Why

Every active account currently sees the same app: members see the whole-gym schedule, and trainers
and admins see "Mój plan", "Moje zajęcia" and a karnet card that make no sense for them. On a
desktop screen an admin cannot reach half of the admin pages from any menu. This slice gives each
role its own app and makes every top-level page reachable from the menu. The rules are enforced in
the UI and the API alike.

## Starting Point

- **No persona gating.** Menus, guards and the `ActiveMember` policy all gate on "is active", never
  on "member or staff". A trainer holds `User`, so no policy can tell them apart from a member.
- **Hidden admin pages.** They are linked only from `/more`, which only the phone's bottom bar links
  to.
- **Club-wide admin dashboard.** It shows every class in the club.
- **Trainer booking without a UI.** The API already lets a trainer book into their own classes, but
  the SPA has no screen for it.

## Desired End State

- **Member:** a dashboard with bookings, karnet and plan. "Zajęcia", "Plan", "Moje konto". No
  schedule.
- **Trainer:** a dashboard of the classes they instruct, a schedule of only those classes where a
  click opens the roster and booking, and their member list. No plan, karnet or bookings of their
  own.
- **Admin and Admin+Trainer:** a dashboard of classes they instruct, and the full schedule (the
  management calendar on desk, the read-only calendar below). Członkowie, Typy zajęć and Ćwiczenia
  are in the header.
- **Staff in the domain:** staff can no longer be given a karnet, a booking or a plan.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Admin's own plan | None (the request's "powinien mieć" was a typo) | Consistent with "no karnet for the admin" | Research Q&A |
| Trainer hides | Own bookings as participant, plan, karnet | A trainer does not train as a client | Research Q&A |
| Enforcement | UI and API | A hidden link does not protect data | Research Q&A |
| Persona precedence | Admin > Trainer > Member, one predicate in nav, guard and policy | Roles are additive, and the "nav equals guard" bug class from S-01 | Plan |
| Staff as participants | Forbidden in the domain (`409 member_is_staff`) | The user chose a coherent model over hide-only | Plan |
| Admin dashboard rule | Only classes the admin instructs | Follows from forbidding staff participation | Plan |
| Granting Trainer to a member with data | Allowed; the data becomes invisible | User choice. No destructive cascade | Plan |
| Existing staff data | No migration; a runbook query lists it for manual clean-up | Keeps the "every migration has a working `Down`" rule | Plan |
| Member calling `GET /api/classes` | 403 | A member has `/api/bookings/mine` | Plan |
| Admin "Grafik" | One entry: `/admin/classes` at ≥64rem, `/schedule` below | Avoids two calendars in the menu and the UX-01 refusal | Plan |
| Trainer dashboard | "Twoje zajęcia" from the same instructed-classes feed | One feed for both staff personas, and the trainer's start screen is not empty | Plan |
| Karnet | Stays a dashboard card, no page | One number and a date | Plan |
| Trainer booking UI | In scope: the existing overlay, moved and parameterised | The API already supports it, and the picker reuses `/api/trainer/members` | Plan |
| Menus | Per-persona table: all links in the header, at most 5 in the bar, the rest in Więcej | Every page one tap away | Plan |
| Seed | `admin2` becomes Admin+Trainer and instructs classes | Makes the new dashboard visible on Staging | Plan |
| Docs | A PRD v2 amendment, a roadmap S-25 entry and an AGENTS.md rule as Phase 1 | Reverses FR-002, FR-007/018, FR-024 and the S-12 bar design | Plan |

## Scope

**In scope:**
- The `MemberOnly` policy.
- A persona-filtered `GET /api/classes`.
- The new `GET /api/trainer/classes`.
- The `member_is_staff` refusals.
- Staff excluded from the trainer member list.
- The SPA persona function, persona guards and navigation table.
- A per-persona header, bar and `/more`.
- Dashboard and schedule per persona, with trainer booking from the schedule.
- The seed change, the runbook query and the documentation.

**Out of scope:**
- Any data migration, or a cascade when the Trainer role is granted.
- A karnet page.
- Any member-facing schedule.
- Changes to `GET /api/admin/classes` or the admin calendar.
- A new trainer search endpoint.
- A visual redesign.
- How quickly a role change reaches an already signed-in session.

## Architecture / Approach

The server policy set gains `MemberOnly`, defined as `User` held without `Trainer` or `Admin`. It
now guards the four `/mine` routes.

The schedule read moves to `TrainerOrAdmin`, and its handler branches the way `MayActOn` already
does: an admin gets every class, anyone else only the classes whose instructor is them.

"Is staff" has one Infrastructure definition, a correlated EXISTS over Identity roles. Two places
share it: `IMemberStore.IsStaffAsync`, which the pass and booking handlers call, and the plan
`Assignable` query.

On the SPA side, `personaOf(user)` feeds the guards and `navigationFor(persona, desk)`. That one
function supplies the header, the role-blind bottom bar (which receives its links as an input) and
`/more`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Record the product decision | PRD amendment, roadmap S-25, AGENTS.md persona rule | Missing a superseded requirement leaves PRD drift for future reviews |
| 2. API: access per persona | `MemberOnly`, filtered schedule, instructed-classes feed, rewritten access tests | Many existing tests read `/api/classes` as a member and must be moved |
| 3. Domain: staff hold nothing | `member_is_staff` on pass, booking and plan; staff-free trainer list; member-list actions; seed | The booking path must stay untouched: the check goes in `BookForMember`, not the protocol |
| 4. SPA: personas, guards, navigation | `personaOf`, member and staff guards, per-persona header, bar and `/more` | Nav, guard and policy predicates drifting apart |
| 5. SPA: screens per persona | Dashboard per persona, staff schedule with the bookings overlay, member empty states | First reuse of an overlay across two features |

**Prerequisites:** S-16 (staff booking API), S-22 (trainer member list), S-24 (seed).
**Estimated effort:** about 4–5 sessions. Phases 2–5 land on one branch and merge together, because
CI deploys every merge to `main`.

## Open Risks & Assumptions

- **Blind spot after granting Trainer.** A member who holds a karnet or bookings and is then granted
  Trainer can no longer see that data. The admin still sees it in rosters and on the member's passes
  screen.
- **Existing staff-held data.** It stays until someone cleans it up by hand, found with the runbook
  query.
- **Search by name only.** The trainer's picker searches by name only, not by e-mail. That is
  accepted.
- **Class details lost.** `ClassDetailsOverlay` is deleted, so staff see a class's description only
  on the admin class form.

## Success Criteria (Summary)

- Each persona, signed in, sees exactly its row of the access matrix. Typed URLs and direct API
  calls outside it are refused.
- On every screen width, an admin reaches every admin page from the menu.
- A trainer runs their own classes, both roster and booking, from `/schedule` without admin help.
