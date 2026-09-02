# Trainer Role and Assignment — Plan Brief

> Full plan: `context/changes/trainer-role-and-assignment/plan.md`

## What & Why

Add a third application role, `Trainer`, that the admin grants to and revokes from active accounts
on the existing member screen. The role is additive and confers no new permission here. It exists
so that "who runs this class" can become a person the system knows — today the instructor is free
text precisely because the product shipped without a trainer role, and `S-06` cannot offer an
instructor selection over people that are not modelled.

## Starting Point

Identity shipped in `F-02` and the member screen in `S-02`. Two roles exist, `User` and `Admin`,
declared in `ApplicationRoles` with a single `All` array. Registration grants `User`; the seeder
grants `Admin` only — so role membership is already a set, and the admin is already not a member.
The admin's member list structurally excludes admins, and blocking one is refused a second time at
the endpoint. The endpoint group has a settled mutation shape (idempotency check → named 409 →
concurrency-stamp rotation → one save) and a contract mirrored field-for-field by the SPA.

## Desired End State

The admin sees every account on the member screen — including their own — each row carrying status
and role badges. A per-row menu grants or revokes `Trainer` on active accounts, and is absent
elsewhere. Granting the admin account works, so an owner who teaches can appear in the instructor
selection later. Blocking an admin is still refused. Nothing changes about what any account can do.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Admins vs. the member list | Show admins with role-only actions | FR-003 needs an owner-who-teaches to be grantable, and the grant surface is that list; the block guard stays as the protection. |
| `ApplicationRoles.All` | Split into a seeded set and a policy set | The one array today drives both seeding and `ActiveMember`, so adding a role to it would silently widen who passes the policy. |
| How the role is written | `UserManager.AddToRoleAsync`, no stamp rotation | The surrounding one-save pattern exists to bind a status flip to its outbox rows; a role grant enqueues nothing, so there is no second write to bind. |
| Session on role change | Nothing — let it expire naturally | The role confers no permission in this change, so a stale claim has no security consequence. |
| Wire format | `roles: string[]` on `MemberSummary` | General enough to cover admin rows and any future role without another contract change. |
| Which accounts are grantable | `Active` only; 409 `not_active` otherwise | Matches FR-001's "approved account" and stops a non-vetted account reaching the instructor selection. |
| Screen affordance | Role badges plus a per-row action menu | Chosen over an in-row toggle; keeps the row readable as actions accumulate. |
| Verification | Endpoint tests plus component tests | Two candidate regression tests were scoped out; manual steps 4 and 7 compensate. |

## Scope

**In scope:** the `Trainer` role constant and its seeding; the role-set split; grant and revoke
endpoints; `roles` on the member-list contract; admins appearing in that list; role badges and a
per-row action menu on the member screen; endpoint and component tests.

**Out of scope:** any trainer-facing screen or permission; the instructor selection itself (`S-06`);
session invalidation on role change; a trainer registration path; a role filter on the list; any
change to `ActiveMember` semantics.

## Architecture / Approach

Backend first, screen second. `ApplicationRoles` grows `Trainer` and splits its single array into
two named sets — one for what the seeder creates, one for what satisfies `ActiveMember` — so that
adding a role can no longer change authorization as a side effect. Two endpoints join the existing
admin group, which already carries the `Admin` policy at group level. `MemberQuery` drops its admin
filter and projects role names; the contract gains `roles`, mirrored in the SPA. The screen renders
badges and a per-row menu, reusing the component's existing per-row `busy` / `failedId` / `notice`
signals and its response-generation guard.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Role, policy, and the admin API | `Trainer` exists and is seeded; grant/revoke endpoints; member list carries roles and includes admins | The role-set split is the one edit that could silently widen `ActiveMember`; the admin guard drops from two layers to one |
| 2. Member screen | Role badges and a per-row action menu on the admin's member list | The SPA has no menu pattern yet — keyboard, focus return, outside-click and mobile behaviour are all new ground |

**Prerequisites:** `S-02` (member-management) archived — the list, the admin policy, the
`Application` → `Infrastructure` query seam and the status index all already exist.

**Estimated effort:** two phases, backend then screen; the second is the larger of the two because
the menu is built from scratch.

## Open Risks & Assumptions

- After this change, `BlockAsync`'s `is_admin` check is the **only** thing stopping the club from
  blocking its own admin. No automated test covers it; manual step 4 does.
- No automated regression test guards `ActiveMember` after the role-set split; manual step 7 does.
  These two were deliberate scoping decisions, recorded so they are checked rather than forgotten.
- A granted role does not reach the holder's own session until the security stamp refreshes. Safe
  now, but a later slice giving `Trainer` real permissions must revisit revocation timing.
- The row menu has no precedent in this SPA, so its accessibility and mobile behaviour are
  unverified by any existing component.

## Success Criteria (Summary)

- The admin grants `Trainer` to an active member and to their own admin account, and both rows show
  the badge.
- Granting is refused on a pending or blocked account, and blocking an admin is still refused.
- No account gains or loses access as a result of this change.
