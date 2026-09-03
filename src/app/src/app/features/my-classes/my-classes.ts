import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { bookingFailureMessage } from '../../core/scheduling/booking-failure';
import { BookingService } from '../../core/scheduling/booking.service';
import { MyBooking } from '../../core/scheduling/booking.models';

/**
 * The member's upcoming bookings (prd.md FR-010).
 *
 * A LIST, DELIBERATELY NOT A CALENDAR. The schedule answers "what is on"; this answers "what am I
 * committed to", which is a short chronological list and reads worse as a grid. It also must not
 * import the calendar at all: `/schedule` and `/admin/classes` are lazy specifically to keep
 * angular-calendar and date-fns out of the initial bundle, and a third screen pulling them in would
 * undo that from a route that has no use for them.
 *
 * Shell shape follows `schedule.ts` — rows, loading, loadFailed, a generation fence — and the
 * per-row action follows `classes.ts`: a `busy` set plus a `failedId`, so one slow cancellation does
 * not disable the rest of the list.
 */
@Component({
  imports: [DatePipe, RouterLink],
  selector: 'app-my-classes',
  styleUrl: './my-classes.scss',
  templateUrl: './my-classes.html',
})
export class MyClasses implements OnInit {
  private readonly bookings = inject(BookingService);

  protected readonly rows = signal<MyBooking[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** Class ids with a cancellation in flight. Keyed by CLASS, because that is what cancel addresses. */
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  /** The row whose cancellation failed, and what to say about it. Cleared when another one starts. */
  protected readonly failedId = signal<string | null>(null);
  protected readonly failure = signal<string | null>(null);

  /**
   * Same fence as `schedule.ts`. There is no navigation here, but a reload racing a first load is
   * still two responses that can land in either order.
   */
  private generation = 0;

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    const generation = ++this.generation;

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const rows = await this.bookings.getMine();

      if (generation !== this.generation) {
        return;
      }

      this.rows.set(rows);
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
   * Releases a spot and removes the row in place.
   *
   * REMOVED, not marked: this list is upcoming ACTIVE bookings, so a cancelled one has no place in
   * it, and refetching to discover that would be a round trip to learn what the response already
   * said. The booking itself survives on the server — FR-009 keeps the history — it is only this
   * list that no longer includes it.
   */
  protected async cancel(row: MyBooking): Promise<void> {
    this.setBusy(row.classId, true);
    this.failedId.set(null);
    this.failure.set(null);

    try {
      await this.bookings.cancel(row.classId);

      this.rows.update((rows) => rows.filter((candidate) => candidate.bookingId !== row.bookingId));
    } catch (error) {
      const reason = ((error as HttpErrorResponse)?.error as { reason?: string } | undefined)
        ?.reason;

      this.failedId.set(row.bookingId);
      this.failure.set(bookingFailureMessage(reason));
    } finally {
      this.setBusy(row.classId, false);
    }
  }

  protected isBusy(classId: string): boolean {
    return this.busy().has(classId);
  }

  /** Derived, never stored — the same rule the Class aggregate follows. */
  protected endsAt(row: MyBooking): Date {
    return new Date(new Date(row.startsAt).getTime() + row.durationMinutes * 60_000);
  }

  private setBusy(classId: string, value: boolean): void {
    this.busy.update((ids) => {
      const next = new Set(ids);
      if (value) {
        next.add(classId);
      } else {
        next.delete(classId);
      }
      return next;
    });
  }
}
