/**
 * The width at which the schedule stops showing one day and shows the whole week (prd-v2 FR-016).
 *
 * THE only definition. There was briefly a `--breakpoint-week` twin in `styles.scss`, on the theory
 * that a stylesheet would need its own copy — but no stylesheet ever read it, and a CSS custom
 * property cannot legally appear in a media condition anyway, so it could never have played that
 * part. The day/week switch is a `matchMedia` read, not a media query, which is why this is a TS
 * constant and why one is enough. A stylesheet that needs the number should take it from here via a
 * host binding rather than growing a second source of truth.
 *
 * 48rem is 768px at the default root size — the first width at which seven day columns stay legible
 * rather than becoming the phone-hostile grid the product PRD ruled out.
 */
export const WEEK_VIEW_MIN_WIDTH = '48rem';

/** The media query the calendar listens to. Built here so the string exists in exactly one place. */
export const WEEK_VIEW_MEDIA_QUERY = `(min-width: ${WEEK_VIEW_MIN_WIDTH})`;
