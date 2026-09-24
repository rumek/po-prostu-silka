import { Component, computed, input } from '@angular/core';

/**
 * The club's time zone. A member's classes are shown and grouped by the GYM's calendar —
 * the way the server pages the history — so a class at 23:30 UTC on the last of a month sits in the
 * next month here too.
 */
export const CLUB_ZONE = 'Europe/Warsaw';

const MONTH_KEY = new Intl.DateTimeFormat('en-CA', {
  timeZone: CLUB_ZONE,
  year: 'numeric',
  month: '2-digit',
});
const MONTH_HEADING = new Intl.DateTimeFormat('pl-PL', {
  timeZone: CLUB_ZONE,
  month: 'long',
  year: 'numeric',
});
const WEEKDAY = new Intl.DateTimeFormat('pl-PL', { timeZone: CLUB_ZONE, weekday: 'short' });
const DAY = new Intl.DateTimeFormat('pl-PL', { timeZone: CLUB_ZONE, day: 'numeric' });
const MONTH_SHORT = new Intl.DateTimeFormat('pl-PL', { timeZone: CLUB_ZONE, month: 'short' });
const LONG_DATE = new Intl.DateTimeFormat('pl-PL', {
  timeZone: CLUB_ZONE,
  weekday: 'long',
  day: 'numeric',
  month: 'long',
});

export const CLOCK = new Intl.DateTimeFormat('pl-PL', {
  timeZone: CLUB_ZONE,
  hour: '2-digit',
  minute: '2-digit',
});

export interface MonthGroup<T> {
  key: string;
  heading: string;
  items: T[];
}

/**
 * Splits an already ordered list into club-local months, keeping the order — the server sorts, and
 * re-sorting here would be a second source of truth for the same rule.
 */
export function groupByMonth<T>(
  items: readonly T[],
  startsAt: (item: T) => string,
): MonthGroup<T>[] {
  const groups: MonthGroup<T>[] = [];

  for (const item of items) {
    const instant = new Date(startsAt(item));
    const key = MONTH_KEY.format(instant);

    let group = groups.at(-1);
    if (!group || group.key !== key) {
      const heading = MONTH_HEADING.format(instant);
      group = { key, heading: heading.charAt(0).toUpperCase() + heading.slice(1), items: [] };
      groups.push(group);
    }

    group.items.push(item);
  }

  return groups;
}

/**
 * A row's date line: "CZW. 10 WRZ", small and in capitals above the class. The short parts are for
 * the eye; a screen reader gets the date said once, in full.
 */
@Component({
  selector: 'app-class-date',
  styleUrl: './class-date.scss',
  template: `
    <span class="class-date-short" aria-hidden="true"
      >{{ weekday() }} {{ day() }} {{ month() }}</span
    >
    <span class="class-date-spoken">{{ spoken() }}</span>
  `,
})
export class ClassDate {
  /** ISO-8601 UTC, as the API returns it. */
  readonly startsAt = input.required<string>();

  private readonly instant = computed(() => new Date(this.startsAt()));

  protected readonly weekday = computed(() => WEEKDAY.format(this.instant()));
  protected readonly day = computed(() => DAY.format(this.instant()));
  protected readonly month = computed(() => MONTH_SHORT.format(this.instant()).replace('.', ''));
  protected readonly spoken = computed(() => LONG_DATE.format(this.instant()));
}
