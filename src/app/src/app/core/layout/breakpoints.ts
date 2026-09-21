/**
 * The width thresholds TypeScript needs — which is ONE (S-20, UX-04).
 *
 * The full set lives in `src/styles/_breakpoints.scss`, because nearly every width decision in this
 * app is layout and belongs to a stylesheet. The desk boundary is the exception: below it the
 * admin's class calendar is not laid out differently but NOT RENDERED, and withholding a subtree is
 * a template decision. So this value exists twice, and `breakpoints.spec.ts` fails the build when
 * the two copies disagree.
 *
 * 64rem is 1024px at the default root size — a laptop, or a tablet held in landscape. A phone in
 * landscape stays below it, which is the point: 30rem, the phone-LAYOUT threshold, would have let a
 * sideways phone into a grid built for a mouse.
 *
 * NOT the calendar's 48rem day/week switch, which answers a different question and lives in
 * `shared/calendar/calendar-breakpoint.ts`.
 */
export const DESK_MIN_WIDTH = '64rem';

/** Built from the constant so the string exists in exactly one place. */
export const DESK_MEDIA_QUERY = `(min-width: ${DESK_MIN_WIDTH})`;

/**
 * A mouse, trackpad or stylus — a pointer precise enough to draw a half-hour block on a grid.
 * Not a width, but read the same way and for the same kind of reason: whether a gesture is on offer.
 */
export const FINE_POINTER_MEDIA_QUERY = '(pointer: fine)';
