---
change_id: pass-paid-flag
title: Mark a karnet as paid or unpaid
status: impl_reviewed
created: 2026-10-02
updated: 2026-10-02
archived_at: null
---

## Notes

Origin: the club's current spreadsheets (`xlsx/twt.xlsx`, sheet "Klienci") carry a "Zapłacono?" column
and a derived "BRAK PŁATNOŚCI" status. The client named marking a karnet as paid the one must-have
for now — the minimal step towards the spreadsheet, not the full payments model (price, method,
payment history, dashboards stay out of scope).

Issuing a karnet is NOT the same fact as it being paid: the spreadsheet records passes with
"Zapłacono? = NIE", and since the karnet gates booking (S-16), equating the two would force the
admin to withhold the pass — and so the booking — until money arrives.

Decisions made with the user (2026-10-02):

- Shape: one nullable `PaidAt` date on `MembershipPass`; null = unpaid. No price, no method, no
  history.
- The "Opłacony" checkbox is **unchecked by default** when issuing a karnet (matches the spreadsheet,
  where an empty cell means unpaid).
- **Admin and trainer** may mark a karnet paid / unpaid. (A trainer may take cash at the gym; the
  plan must decide which karnets a trainer may touch — e.g. members of classes they instruct, by the
  same rule as `BookingAuthorization.MayActOn` — or all.)
- **The member sees the payment status** of their own karnet.
- An unpaid karnet does **not** block booking — it is only marked, as in the spreadsheet.
- Staff see an "Nieopłacony" marker on the member card and list, with a filter for unpaid.
- Migration: existing passes get `PaidAt = IssuedAt`, so nobody turns into a debtor on deploy;
  working `Down`.
- Answers to the research's open questions (2026-10-02):
  1. "Nieopłacony" on the member list (marker and filter) means **any** unpaid karnet, not just
     today's: a debt outlives the karnet's validity, as with the spreadsheet's "BRAK PŁATNOŚCI".
  2. A trainer may mark the karnet of **any** non-staff member. This matches `/api/trainer/members`,
     which is already unscoped. There is no new trainer-to-member relation.
  3. **Record who marked it paid**: `PaidRecordedBy` (user id, no FK), following
     `Booking.AttendanceRecordedBy`.
  4. The payment date is **editable and defaults to today** (club-local). It may not be in the future.
- PRD: needs a one-sentence amendment to the "no payments" Non-Goal — the app records that a karnet
  was paid, never takes payment, never stores amounts.
