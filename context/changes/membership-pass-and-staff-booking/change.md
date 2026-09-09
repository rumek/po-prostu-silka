---
change_id: membership-pass-and-staff-booking
title: The karnet decides who trains — staff-only booking, no approval gate
status: implementing
created: 2026-09-09
updated: 2026-09-09
archived_at: null
---

## Notes

M-4's only slice (roadmap S-16). Three moves that are one product decision:

1. Self-service booking is removed. Only an admin, and a trainer for the classes they personally
   instruct, may book a member into a class or release a spot. A member gets a read-only view of
   their upcoming classes.
2. Admin approval of new accounts is removed, along with the Zgłoszenia tab and the
   awaiting-approval screen. Registration produces an active account; blocking stays.
3. A `MembershipPass` (karnet) belongs to a member: type name, inclusive validity range, and an
   entry count that is always required. Nobody can be booked into a class unless the member holds a
   pass valid **on the day of the class** with a free entry. Entries are consumed by active bookings
   and returned on release — derived, never a stored counter. Passes form a history and may not
   overlap.

Settled with the user before planning; see the M-4 scope anchors MP-01–MP-07 in
`context/foundation/roadmap.md`. Approved design notes live in the session plan and are the input to
`/10x-plan`.

## Status note

All eight phases are implemented and every AUTOMATED success criterion in `plan.md` passes (514
backend integration tests, 422 frontend unit tests, a warning-free build, and a 504 kB initial bundle
against a 550 kB budget). `status` stays `implementing` rather than `implemented` because the plan's
24 manual-verification rows are deliberately outstanding — the user reserved them for one pass at the
end of the slice. Flip to `implemented` once they are ticked.
