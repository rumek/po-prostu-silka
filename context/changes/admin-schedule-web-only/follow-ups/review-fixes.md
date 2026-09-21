# Review follow-ups — admin-schedule-web-only

From `reviews/impl-review.md` (2026-09-21).

## F7 — manual verification before archiving

These steps need a person: a real `angular-calendar`, a touch tablet, and real widths. Tick them in
`plan.md` Progress as they pass.

- [ ] 1.5–1.6: the nav partition at 480px (and fractional zoom), and the exercise form's two columns from 640px up.
- [ ] 2.4–2.6: the refusal on a phone (portrait and landscape); resizing across 1024px both ways with an overlay open.
- [ ] 3.4–3.5: draw, drag and resize with a mouse; on a touch tablet in landscape, the grid scrolls, a tap draws nothing, and a long-press move still works.
- [ ] 4.5–4.9: every flow from the overlay on a 30-minute class; drag and resize never open it, while a click and Enter do; past weeks stay inert.
- [ ] Added by the review fixes: close an overlay while a request is in flight, open another class, and it stays open (F1); a week count of 20 is refused under the field (F3).
