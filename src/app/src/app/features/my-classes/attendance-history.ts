import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import { AttendanceOutcome, MyAttendanceEntry } from '../../core/scheduling/booking.models';
import { BookingService } from '../../core/scheduling/booking.service';
import { createLoadFence } from '../../shared/forms/load-fence';
import { Loading } from '../../shared/forms/loading/loading';
import { Empty } from '../../shared/forms/empty/empty';
import { Icon, IconName } from '../../shared/icons/icon';
import { List } from '../../shared/list/list';
import { Row } from '../../shared/list/row';
import { BookedClass } from '../../shared/class-date/booked-class';
import { groupByMonth } from '../../shared/class-date/class-date';

/** The word and glyph for each outcome. The word carries the meaning; colour and glyph repeat it. */
export const OUTCOME_LABELS: Record<AttendanceOutcome, { word: string; icon: IconName }> = {
  present: { word: 'Obecny', icon: 'present' },
  absent: { word: 'Nieobecny', icon: 'absent' },
  unrecorded: { word: 'Nie odnotowano', icon: 'unrecorded' },
  cancelled: { word: 'Odwołane', icon: 'cancelled' },
};

export interface HistoryMonth {
  key: string;
  heading: string;
  entries: MyAttendanceEntry[];

  /** Classes the member came to. */
  attended: number;

  /** Classes that count: present or absent. Cancelled and unrecorded are neither a yes nor a no. */
  counted: number;
}

/**
 * The member's attendance history (S-27, AT-04, AT-05).
 *
 * <h2>Read at a glance, not read out</h2>
 *
 * AT-05's requirement is that this is NOT one sentence per class. So: one section per club-local
 * month with its own tally, and rows that are a date column, a name and — for a class attended or
 * missed only — a V or X dot, with the word behind it for screen readers and as its tooltip. An
 * unrecorded class gets no dot: it is neither a yes nor a no. A cancelled one is crossed out. The karnet's own summary card was
 * dropped: the dashboard already carries the balance, and this tab is about the classes.
 *
 * The upcoming tab next door follows the same row (`shared/class-date/booked-class.ts`) and month layout.
 *
 * <h2>Its own load</h2>
 *
 * It is rendered only once its tab is first selected, and it fetches on init — which is what makes
 * the history lazy. One fence for the first page, per AGENTS.md: it is independent of the upcoming
 * list next door. "Pokaż wcześniejsze" appends, and its failure stays under the button, because the
 * months already loaded remain perfectly readable (outlet 2 rather than outlet 4).
 */
@Component({
  imports: [BookedClass, Loading, Empty, Icon, List, Row],
  selector: 'app-attendance-history',
  styleUrl: './attendance-history.scss',
  templateUrl: './attendance-history.html',
})
export class AttendanceHistory implements OnInit {
  private readonly bookings = inject(BookingService);

  protected readonly labels = OUTCOME_LABELS;

  protected readonly items = signal<MyAttendanceEntry[]>([]);
  protected readonly earlierBefore = signal<string | null>(null);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly loadMessage = signal<string | null>(null);

  protected readonly loadingMore = signal(false);
  protected readonly moreFailure = signal<string | null>(null);

  private readonly fence = createLoadFence();

  protected readonly groups = computed<HistoryMonth[]>(() =>
    groupByMonth(this.items(), (entry) => entry.startsAt).map((group) => ({
      key: group.key,
      heading: group.heading,
      entries: group.items,
      counted: group.items.filter((e) => e.outcome === 'present' || e.outcome === 'absent').length,
      attended: group.items.filter((e) => e.outcome === 'present').length,
    })),
  );

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
