import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import {
  AttendanceOutcome,
  MyAttendanceEntry,
  MyAttendanceSummary,
} from '../../core/scheduling/booking.models';
import { BookingService } from '../../core/scheduling/booking.service';
import { createLoadFence } from '../../shared/forms/load-fence';
import { Loading } from '../../shared/forms/loading/loading';
import { Empty } from '../../shared/forms/empty/empty';
import { Icon, IconName } from '../../shared/icons/icon';
import { List } from '../../shared/list/list';
import { Row } from '../../shared/list/row';

/**
 * The club's time zone. History is grouped and shown by the GYM's calendar, the way the server
 * pages it, so a class at 23:30 UTC on the last of a month sits in the next month here too.
 */
const CLUB_ZONE = 'Europe/Warsaw';

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
const DAY = new Intl.DateTimeFormat('pl-PL', { timeZone: CLUB_ZONE, day: 'numeric' });
const WEEKDAY = new Intl.DateTimeFormat('pl-PL', { timeZone: CLUB_ZONE, weekday: 'short' });
const TIME = new Intl.DateTimeFormat('pl-PL', {
  timeZone: CLUB_ZONE,
  hour: '2-digit',
  minute: '2-digit',
});

/** A date-only string (YYYY-MM-DD) is a calendar day, not an instant: formatted in UTC so it never shifts. */
const DAY_MONTH = new Intl.DateTimeFormat('pl-PL', {
  timeZone: 'UTC',
  day: 'numeric',
  month: 'long',
});

/** Past this many entries the dot strip stops being a picture and becomes noise, so it is left out. */
const MAX_DOTS = 30;

/** The word and glyph for each outcome. The word carries the meaning; colour and glyph repeat it. */
export const OUTCOME_LABELS: Record<AttendanceOutcome, { word: string; icon: IconName }> = {
  present: { word: 'Obecny', icon: 'present' },
  absent: { word: 'Nieobecny', icon: 'absent' },
  unrecorded: { word: 'Nie odnotowano', icon: 'unrecorded' },
  cancelled: { word: 'Odwołane', icon: 'cancelled' },
};

export interface HistoryRow {
  entry: MyAttendanceEntry;
  day: string;
  weekday: string;
  time: string;
}

export interface MonthGroup {
  key: string;
  heading: string;
  rows: HistoryRow[];

  /** Classes the member came to. */
  attended: number;

  /** Classes that count: present or absent. Cancelled and unrecorded are neither a yes nor a no. */
  counted: number;
}

type Dot = 'present' | 'unrecorded' | 'free';

/**
 * The member's attendance history (S-27, AT-04, AT-05).
 *
 * <h2>Read at a glance, not read out</h2>
 *
 * AT-05's requirement is that this is NOT one sentence per class. So: a summary card for the current
 * karnet on top, then one section per club-local month with its own tally, and rows that are a date
 * column, a name and a status chip. The chip is a word with an icon — the word carries the meaning,
 * so neither the colour nor the glyph is ever the only signal.
 *
 * <h2>Its own load</h2>
 *
 * It is rendered only once its tab is first selected, and it fetches on init — which is what makes
 * the history lazy. One fence for the first page, per AGENTS.md: it is independent of the upcoming
 * list next door. "Pokaż wcześniejsze" appends, and its failure stays under the button, because the
 * months already loaded remain perfectly readable (outlet 2 rather than outlet 4).
 */
@Component({
  imports: [Loading, Empty, Icon, List, Row],
  selector: 'app-attendance-history',
  styleUrl: './attendance-history.scss',
  templateUrl: './attendance-history.html',
})
export class AttendanceHistory implements OnInit {
  private readonly bookings = inject(BookingService);

  protected readonly labels = OUTCOME_LABELS;

  protected readonly summary = signal<MyAttendanceSummary | null>(null);
  protected readonly items = signal<MyAttendanceEntry[]>([]);
  protected readonly earlierBefore = signal<string | null>(null);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly loadMessage = signal<string | null>(null);

  protected readonly loadingMore = signal(false);
  protected readonly moreFailure = signal<string | null>(null);

  private readonly fence = createLoadFence();

  protected readonly groups = computed<MonthGroup[]>(() => {
    const groups: MonthGroup[] = [];

    for (const entry of this.items()) {
      const instant = new Date(entry.startsAt);
      const key = MONTH_KEY.format(instant);

      let group = groups.at(-1);
      if (!group || group.key !== key) {
        const heading = MONTH_HEADING.format(instant);
        group = {
          key,
          heading: heading.charAt(0).toUpperCase() + heading.slice(1),
          rows: [],
          attended: 0,
          counted: 0,
        };
        groups.push(group);
      }

      group.rows.push({
        entry,
        day: DAY.format(instant),
        weekday: WEEKDAY.format(instant),
        time: TIME.format(instant),
      });

      if (entry.outcome === 'present' || entry.outcome === 'absent') {
        group.counted++;
      }
      if (entry.outcome === 'present') {
        group.attended++;
      }
    }

    return groups;
  });

  /** The summary's end date, "do 30 września". */
  protected readonly validTo = computed(() => {
    const summary = this.summary();

    return summary ? DAY_MONTH.format(new Date(`${summary.validTo}T00:00:00Z`)) : '';
  });

  /**
   * One dot per class the karnet paid for that already happened, then one hollow dot per entry
   * still unused — a picture of the pass. Decorative: the counts beside it carry the meaning.
   *
   * An absence takes NO dot: it returned its entry (AT-03), so drawing it would show the pass fuller
   * than the balance on the dashboard says it is.
   */
  protected readonly dots = computed<Dot[]>(() => {
    const summary = this.summary();
    if (!summary || summary.entryCount > MAX_DOTS) {
      return [];
    }

    const filled: Dot[] = [
      ...Array<Dot>(summary.present).fill('present'),
      ...Array<Dot>(summary.unrecorded).fill('unrecorded'),
    ].slice(0, summary.entryCount);

    return [...filled, ...Array<Dot>(summary.entryCount - filled.length).fill('free')];
  });

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();

    this.loading.set(true);
    this.loadFailed.set(false);
    this.moreFailure.set(null);

    try {
      const page = await this.bookings.getHistory();

      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.summary.set(page.summary);
      this.items.set(page.items);
      this.earlierBefore.set(page.earlierBefore);
    } catch (failure) {
      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.loadMessage.set(transportMessage(classifyFailure(failure)));
      this.loadFailed.set(true);
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.loading.set(false);
      }
    }
  }

  /** Appends the next three months. The button is gone once there is nothing older. */
  protected async loadEarlier(): Promise<void> {
    const before = this.earlierBefore();
    if (!before || this.loadingMore()) {
      return;
    }

    const generation = this.fence.begin();
    this.loadingMore.set(true);
    this.moreFailure.set(null);

    try {
      const page = await this.bookings.getHistory(before);

      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.items.update((items) => [...items, ...page.items]);
      this.earlierBefore.set(page.earlierBefore);
    } catch (failure) {
      if (this.fence.isCurrent(generation)) {
        this.moreFailure.set(
          transportMessage(classifyFailure(failure)) ??
            'Nie udało się wczytać wcześniejszych zajęć. Spróbuj ponownie.',
        );
      }
    } finally {
      this.loadingMore.set(false);
    }
  }
}
