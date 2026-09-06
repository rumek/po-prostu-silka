# Member Profile Edit — Plan Brief

> Full plan: `context/changes/member-profile-edit/plan.md`
> Research: `context/changes/member-profile-edit/research.md`

## What & Why

Members register with three fields and then have no account surface at all — nothing to view, nothing
to edit, and no way back in if they forget their password. This slice collects phone number and
address at registration, gives members a profile screen where those details (and their password) can
be changed, and adds a forgot/reset-password flow reachable before login.

## Starting Point

The member is `ApplicationUser : IdentityUser` — one row in `AspNetUsers`, no separate entity.
Registration takes `(Email, Password, DisplayName)` with hand-rolled validation and a closed
vocabulary of failure codes the SPA maps onto form controls. No profile endpoint and no password
endpoints exist. Identity's token providers **are** already registered, and a transactional email
outbox already ships the "account approved" notification — so both password flows build on working
infrastructure rather than new dependencies.

## Desired End State

A new member supplies phone and address alongside name, email and password. Once signed in they open
a profile screen from the navigation, see their name and email as read-only text, edit their contact
details, and change their password without losing their session. A member who forgot their password
requests a reset link from the login screen and sets a new one by following it.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| First/last name storage | Keep single `DisplayName` | `register.html` already labels it "Imię i nazwisko", so no schema split is needed to make it read-only. | Plan |
| Name editability | Read-only for the member; FR-006 rewritten | The gym owns the name on the membership; PRD is corrected rather than quietly contradicted. | Plan |
| Phone column | Reuse `IdentityUser.PhoneNumber` | One fewer column, but it must be given an explicit `HasMaxLength` or it lands as `nvarchar(max)`. | Plan |
| Field requirement | Required at registration **and** on profile save | DB nullability exists only to tolerate pre-existing rows, which are prompted to complete. | Plan |
| Validation | Polish formats, server and client | Server stays the authority and returns its own reason codes; the client mirrors the rules for feedback. | Plan |
| Admin visibility | Out of scope | Keeps the slice from growing a member-detail screen that does not exist today. | Plan |
| Reset mechanism | Emailed link with token in the query string | The shape members expect; token characters are unsafe in a path segment. | Plan |
| Link's base URL | New `App:BaseUrl` config key | The outbox renders at enqueue and retries have no request, so `Host` is neither available nor trustworthy. | Plan |
| Local mail | Development-only logging `IEmailSender` | Without it the reset link is unreachable on a dev machine, since ACS reports a permanent failure when unconfigured. | Research |
| Account disclosure | Identical response for every address | Matches the login non-disclosure discipline; F-02's review already flagged a timing oracle of this shape. | Research |
| Rate limiting | Fixed window on `/forgot-password` only | It is the one anonymous endpoint that sends mail on demand, and nothing else in the app is throttled. | Plan |
| Account status | Reset works for Pending and Blocked alike | Branching on status reintroduces the enumeration oracle the previous decision removes. | Plan |
| Token lifespan | Identity's default 24 hours | No custom provider to write; the exposure is accepted. | Plan |
| Session after change | `RefreshSignInAsync` — keep current, drop others | Stamp rotation would otherwise sign out the very member who just changed their password. | Plan |
| Scope vs PRD | Extend S-13 and amend the PRD | Shipping past a documented scope without correcting it is the drift `lessons.md` warns about. | Plan |

## Scope

**In scope:** five contact fields on the member; migration; extended registration contract and form;
`PUT /api/profile` and a member profile screen; `POST /api/auth/change-password`; the full
forgot/reset flow (config, dev sender, outbox notification, throttle, rate limiter, two endpoints,
two screens, login link); PRD, roadmap and lessons updates.

**Out of scope:** splitting `DisplayName`; editing name or email anywhere, by anyone; admin-facing
contact fields or a member-detail screen; email confirmation, 2FA, social login, lockout tuning; a
custom token lifespan; a global rate limiter; backfilling existing accounts; HTML email or templating.

## Architecture / Approach

Contact details live as properties on `ApplicationUser`, validated by one shared `ContactDetails`
helper in `Application` that both the registration endpoint and the profile endpoint call — that
single seam is what stops the two from drifting. Reads ride the existing `CurrentUser` payload from
`/me`, so the SPA needs no extra round trip. The reset email is rendered at enqueue time by a new
notification and delivered by the existing outbox worker; the token comes from Identity's already-
registered default provider and its single-use guarantee is the security stamp, not the send.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Schema and registration | Five fields on the entity, migration, extended registration contract and form | Reused `PhoneNumber` silently stays `nvarchar(max)` if not explicitly configured |
| 2. Profile read and edit | `PUT /api/profile`, extended `CurrentUser`, profile screen, nav entry | Pre-existing accounts with NULLs must stay able to save |
| 3. Change password | `POST /api/auth/change-password` and the profile screen's password section | Missing `RefreshSignInAsync` silently signs out the caller two minutes later |
| 4. Forgot and reset | Config, dev sender, notification, throttle, limiter, two endpoints, two screens | Any asymmetry between known and unknown addresses reintroduces account enumeration |
| 5. Documents | PRD, roadmap and lessons brought in line | None — read-through only |

**Prerequisites:** F-02 (auth identity foundation), S-01 (registration and approval), F-03
(notification delivery) — all shipped. Local SQL Server via `docker compose up -d` for tests.

**Estimated effort:** roughly 4–5 sessions; Phase 4 is the largest by a clear margin.

## Open Risks & Assumptions

- Nobody can correct a typo in a member's name after this slice — not the member, not the admin. An
  admin-side edit path is the obvious follow-up.
- The per-email throttle is in-memory, so it is only correct while the app runs single-instance. That
  matches the current Basic-tier App Service and would break on scale-out.
- `App__BaseUrl` is a new production setting. If it is not set at deploy time, reset links are wrong
  or empty — the handler logs an error but still answers normally, so the failure is silent to users.
- Reset emails inherit the ACS managed-domain deliverability risk already recorded in F-03.
- A 24-hour token means a link sitting in a compromised mailbox is usable for a day.

## Success Criteria (Summary)

- A member registering today ends up with complete contact details on file, and can change them later
  without help.
- A member who forgets their password can recover it unaided, from the login screen, in one round
  trip through their inbox.
- Neither the forgot-password screen nor the endpoint behind it reveals whether an address belongs to
  a member.
