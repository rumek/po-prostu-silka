---
change_id: expiring-passes-dashboard
title: Admin dashboard card listing karnets that end within 5 days
status: implementing
created: 2026-10-02
updated: 2026-10-02
archived_at: null
---

## Notes

Origin: the club's spreadsheet (`xlsx/twt.xlsx`, sheet "Klienci") derives a "KOŃCZY SIĘ" status five
days before a karnet's end date, so that nobody misses a renewal. The client wants the same signal in
the app.

Decisions made with the user (2026-10-02):

- **Shape:** a card on the staff dashboard, "Kończą się karnety". It lists up to 5 members, nearest end
  first: name, "za N dni / jutro / dziś" and the end date. Each row leads to that member's karnet
  screen (`/admin/members/:id/passes`). When more than 5 qualify, a "Zobacz wszystkich (N)" link opens
  `/admin/members` with an expiring filter. With none, the card stays and shows a compact `app-empty`.
- **Persona: admin only.** The trainer has no member list with karnets to link to.
- **"Ending" is about the end date only.** It is tied to payments, not usage: entries running low do
  NOT count.
- **Already-expired karnets are not shown.** No "WYGASŁ" line.
- **The threshold is a fixed 5 days**, as in the spreadsheet; not a setting.
- **Unpaid karnets are NOT combined into this card.** That stays with `pass-paid-flag`.

Constraints and points for `/10x-plan`:

- **A renewed member must not be listed.** "Ending" = the karnet covering today ends within 5 days
  (club-local, `ClubTime`) AND the member has no karnet starting after it. The spreadsheet holds one row
  per client and cannot know about a renewal issued in advance; the app can.
- **One predicate for both surfaces.** The card's count and the list filter must use one Infrastructure
  definition, in the way `StaffPredicate` is shared, so that the number on the card always equals the
  filtered list.
- **Sequenced after `pass-paid-flag`.** That change introduces a karnet filter on the member list (and
  its URL parameter); this one reuses the mechanism rather than inventing a second.
- **The dashboard is eager.** The bundle is 576.29 kB, 24 kB under the warning (AGENTS.md). Measure the
  card before it lands.
- **Staff members hold no karnet** (S-25), so they are excluded by construction. Blocked members: the
  plan decides.
