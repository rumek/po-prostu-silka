import { CLUB_ZONE } from '../../shared/class-date/class-date';

// en-CA formats a date as YYYY-MM-DD, which is exactly the shape a date input and the API's DateOnly
// both take — so nothing is parsed back through `Date`, which would reintroduce a timezone shift.
const ISO_DAY = new Intl.DateTimeFormat('en-CA', {
  timeZone: CLUB_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
});

/**
 * Today at the GYM, as `YYYY-MM-DD` (pass-paid-flag) — the default and the upper bound of a payment
 * date. The club's calendar, not the browser's: the server refuses a payment dated after the
 * club-local today, and a browser in another zone would otherwise offer a day the API calls the future.
 */
export function clubToday(now: Date = new Date()): string {
  return ISO_DAY.format(now);
}
