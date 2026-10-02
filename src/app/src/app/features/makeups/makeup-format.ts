import { CLUB_ZONE, CLOCK, LONG_DATE } from '../../shared/class-date/class-date';
import { MakeupStatus } from '../../core/scheduling/makeup.models';

/** "10 września" in the club's calendar. */
const DAY = new Intl.DateTimeFormat('pl-PL', {
  day: 'numeric',
  month: 'long',
  timeZone: CLUB_ZONE,
});

/** A club-local date (YYYY-MM-DD) as "10 września". Noon UTC lands on that date in Warsaw all year. */
export function clubDay(isoDate: string): string {
  return DAY.format(new Date(`${isoDate}T12:00:00Z`));
}

/** An instant as "10 września" in the club's calendar. */
export function instantDay(instant: string): string {
  return DAY.format(new Date(instant));
}

/** An instant as "czwartek, 10 września · 18:00" in the club's calendar. */
export function instantWhen(instant: string): string {
  const at = new Date(instant);
  return `${LONG_DATE.format(at)} · ${CLOCK.format(at)}`;
}

/** The status word on a row — the spreadsheet's own vocabulary. */
export const STATUS_WORDS: Record<MakeupStatus, string> = {
  open: 'Do odrobienia',
  planned: 'Zaplanowane',
  made_up: 'Odrobione',
  not_made_up: 'Nie odrobione',
};
