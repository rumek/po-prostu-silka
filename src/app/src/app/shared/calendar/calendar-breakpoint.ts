/**
 * The width at which the schedule stops showing one day and shows the whole week (prd-v2 FR-016).
 *
 * KEPT IN TWO PLACES ON PURPOSE, and they must move together: this constant is what `matchMedia`
 * needs, and a CSS custom property is what a stylesheet needs. Neither can read the other. The twin
 * is `--breakpoint-week` in `src/styles.scss`.
 *
 * 48rem is 768px at the default root size — the first width at which seven day columns stay legible
 * rather than becoming the phone-hostile grid the product PRD ruled out.
 */
export const WEEK_VIEW_MIN_WIDTH = '48rem';

/** The media query the calendar listens to. Built here so the string exists in exactly one place. */
export const WEEK_VIEW_MEDIA_QUERY = `(min-width: ${WEEK_VIEW_MIN_WIDTH})`;
