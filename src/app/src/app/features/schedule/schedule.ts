import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { bookingFailureMessage } from '../../core/scheduling/booking-failure';
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
 * <h2>What S-08 added</h2>
 *
 * Tapping a class opens a detail overlay with the booking action. The screen holds the member's own
 * bookings as a set of class ids, so a tile knows whether the caller is in it without the shared
 * `ScheduledClass` projection growing a `bookedByMe` field — splitting the member and admin
 * projections was weighed and declined in S-06, and adding a member-only field would be the same
 * decision by the back door.
 *
 * A booking or a cancellation REPLACES the matching row with the class the server returned, so the
 * tile's spot count moves without refetching the week. That is the whole reason those two endpoints
 * answer with a class rather than a booking.
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
   * Loaded once, then maintained locally by book and cancel — the two operations that can change it
   * — rather than refetched after each. A set rather than the bookings themselves because that is
   * all this screen asks: "is the caller in this class?". The list of bookings belongs to
   * /my-classes.
   */
  protected readonly bookedClassIds = signal<ReadonlySet<string>>(new Set());

  /** The class whose overlay is open, or null. */
  protected readonly selected = signal<ScheduledClass | null>(null);

  /** A booking action is in flight. */
  protected readonly acting = signal(false);

  /** The refusal to show inside the overlay, already in Polish. */
  protected readonly actionError = signal<string | null>(null);

  /** The window the calendar is showing. Null until its first emission, which is the first load. */
  private readonly range = signal<CalendarRange | null>(null);

  /**
   * NEW IN S-07, and not optional. Nothing cancels an in-flight request, so the last RESPONSE would
   * otherwise win: two quick taps on "next week" can land their responses in either order, and the
   * loser would overwrite the week actually on screen. The single fetch this screen used to do could
   * not race with anything; navigation is what made it possible. Same guard as classes.ts.
   *
   * S-08 gave it a second job: a booking that resolves after the member has navigated away must not
   * write its row back into a week that no longer contains it.
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
    this.actionError.set(null);
    this.selected.set(row);
  }

  protected closeDetails(): void {
    this.selected.set(null);
    this.actionError.set(null);
  }

  protected isBooked(id: string): boolean {
    return this.bookedClassIds().has(id);
  }

  protected book(): void {
    const row = this.selected();

    if (row) {
      void this.act(row, () => this.bookings.book(row.id), true);
    }
  }

  protected cancel(): void {
    const row = this.selected();

    if (row) {
      void this.act(row, () => this.bookings.cancel(row.id), false);
    }
  }

  /**
   * Runs a booking write and applies its result in place.
   *
   * Both operations have the same shape — call, replace the row with what came back, move the id in
   * or out of the set, map a refusal to a sentence — so they share one path rather than two that
   * drift.
   *
   * <h2>The generation fence</h2>
   *
   * Checked after the await for the same reason `load` checks it: the member may have navigated to
   * another week while this was in flight, and writing the row back would resurrect a class that is
   * no longer on screen. The booking still HAPPENED — this only declines to redraw for it.
   */
  private async act(
    row: ScheduledClass,
    write: () => Promise<ScheduledClass>,
    nowBooked: boolean,
  ): Promise<void> {
    const generation = this.generation;

    this.acting.set(true);
    this.actionError.set(null);

    try {
      const updated = await write();

      if (generation !== this.generation) {
        return;
      }

      this.rows.update((rows) =>
        rows.map((candidate) => (candidate.id === updated.id ? updated : candidate)),
      );

      this.bookedClassIds.update((ids) => {
        const next = new Set(ids);
        if (nowBooked) {
          next.add(row.id);
        } else {
          next.delete(row.id);
        }
        return next;
      });

      // The overlay stays OPEN and shows the new state — booked, one fewer spot. Closing it would
      // hide the only confirmation the member gets, on the surface they are already looking at.
      this.selected.set(updated);
    } catch (failure) {
      if (generation !== this.generation) {
        return;
      }

      const reason = ((failure as HttpErrorResponse)?.error as { reason?: string } | undefined)
        ?.reason;

      // Shown in the OVERLAY, not as a screen-level banner: the refusal is about the class the
      // member is looking at, and a banner above a calendar would be read as being about the week.
      this.actionError.set(bookingFailureMessage(reason));
    } finally {
      if (generation === this.generation) {
        this.acting.set(false);
      }
    }
  }
}
