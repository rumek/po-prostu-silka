import { Component, inject, signal } from '@angular/core';
import { BookingService } from '../../core/scheduling/booking.service';
import { ClassService } from '../../core/scheduling/class.service';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { CalendarRange, ScheduleCalendar } from '../../shared/calendar/schedule-calendar';
import { ClassDetailsOverlay } from './class-details-overlay/class-details-overlay';

/**
 * The member's schedule (prd.md FR-007, FR-008, FR-009; prd-v2 FR-015, FR-016, FR-018).
 *
 * A CALENDAR since S-07, not the day-grouped list it was: one day at a time on a phone, the whole
 * week from 48rem up. The grouping this screen used to do in a `computed` moved into the shared
 * calendar, which is also what the admin panel renders — FR-017's whole point is that there is one
 * of them.
 *
 * <h2>Browsing only, since S-16</h2>
 *
 * Tapping a class opens a detail overlay — with no action in it. MP-01 removed self-service booking,
 * so this screen reads the schedule and nothing else; the `act()` path that used to apply a booking
 * result back into the week went with it.
 *
 * The screen still holds the member's own bookings as a set of class ids, so a tile can show that the
 * caller is in it, without the shared `ScheduledClass` projection growing a `bookedByMe` field —
 * splitting the member and admin projections was weighed and declined in S-06, and adding a
 * member-only field would be the same decision by the back door. It is now loaded and never
 * modified, because nothing on this screen can change it.
 */
@Component({
  imports: [ClassDetailsOverlay, ScheduleCalendar],
  selector: 'app-schedule',
  styleUrl: './schedule.scss',
  templateUrl: './schedule.html',
})
export class Schedule {
  private readonly classes = inject(ClassService);
  private readonly bookings = inject(BookingService);

  protected readonly rows = signal<ScheduledClass[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /**
   * Class ids the member currently holds an active booking on.
   *
   * Refetched with every window change and never modified locally — since S-16 nothing on this
   * screen can change it. A set rather than the bookings themselves because that is all this screen
   * asks: "is the caller in this class?". The list of bookings belongs to /my-classes.
   */
  protected readonly bookedClassIds = signal<ReadonlySet<string>>(new Set());

  /** The class whose overlay is open, or null. */
  protected readonly selected = signal<ScheduledClass | null>(null);

  /** The window the calendar is showing. Null until its first emission, which is the first load. */
  private readonly range = signal<CalendarRange | null>(null);

  /**
   * NEW IN S-07, and not optional. Nothing cancels an in-flight request, so the last RESPONSE would
   * otherwise win: two quick taps on "next week" can land their responses in either order, and the
   * loser would overwrite the week actually on screen. The single fetch this screen used to do could
   * not race with anything; navigation is what made it possible. Same guard as classes.ts.
   */
  private generation = 0;

  /** Driven by the calendar's rangeChange, which fires on init too — hence no ngOnInit. */
  protected async load(range: CalendarRange): Promise<void> {
    this.range.set(range);

    // A window change invalidates the open overlay: its class may not even be on screen any more.
    this.closeDetails();

    const generation = ++this.generation;

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      // In parallel, and the bookings are fetched on EVERY window change rather than once on init.
      // They are unbounded by window — a member has a handful of upcoming bookings, not a page of
      // them — but refetching is what keeps the set honest when the same tab is left open across a
      // cancellation made elsewhere.
      const [rows, mine] = await Promise.all([
        this.classes.getSchedule(range.from, range.to),
        this.bookings.getMine(),
      ]);

      if (generation !== this.generation) {
        return;
      }

      this.rows.set(rows);
      this.bookedClassIds.set(new Set(mine.map((booking) => booking.classId)));
    } catch {
      if (generation !== this.generation) {
        return;
      }
      this.loadFailed.set(true);
    } finally {
      if (generation === this.generation) {
        this.loading.set(false);
      }
    }
  }

  /**
   * Refetches the window on screen.
   *
   * The member's only recovery from a failed load. The calendar owns navigation, so there is no
   * gesture that would retry as a side effect — without this the answer to a dropped connection is
   * "reload the page", which is not an answer a schedule should give.
   */
  protected async reload(): Promise<void> {
    const range = this.range();

    if (range) {
      await this.load(range);
    }
  }

  protected openDetails(row: ScheduledClass): void {
    this.selected.set(row);
  }

  protected closeDetails(): void {
    this.selected.set(null);
  }

  protected isBooked(id: string): boolean {
    return this.bookedClassIds().has(id);
  }
}
