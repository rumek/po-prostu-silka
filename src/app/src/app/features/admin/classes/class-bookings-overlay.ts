import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { bookingFailureMessage } from '../../../core/scheduling/booking-failure';
import { BookingService } from '../../../core/scheduling/booking.service';
import { ClassBooking } from '../../../core/scheduling/booking.models';
import { ScheduledClass } from '../../../core/scheduling/class.models';

/**
 * Who signed up for a class, and the action to release a spot (prd.md FR-014).
 *
 * <h2>An overlay, not a panel below the calendar</h2>
 *
 * It shipped as a panel first, matching the duplicate and delete flows, and that was the wrong
 * shape: those two are one line and one button, while this is an unbounded LIST. A class near
 * capacity pushed itself off the bottom of the screen, so the admin scrolled away from the calendar —
 * and from the class the list belongs to — to read it. An overlay keeps the whole list in view and
 * names its subject in the title.
 *
 * <h2>It loads its own data</h2>
 *
 * Like `class-create-overlay`, which fetches its own types and trainers. The screen keeps only "which
 * class is open"; opening and closing then costs no state on a component that already tracks four
 * other panels. The one thing that DOES go back to the screen is `released` — the calendar tile
 * behind this overlay shows a spot count, and it must move when the list does.
 */
@Component({
  // On the host, not on the panel: Escape has to close the overlay wherever focus is, including
  // before the admin has touched anything.
  host: { '(document:keydown.escape)': 'close()' },
  imports: [DatePipe],
  selector: 'app-class-bookings-overlay',
  styleUrl: './class-bookings-overlay.scss',
  templateUrl: './class-bookings-overlay.html',
})
export class ClassBookingsOverlay implements OnInit {
  private readonly bookings = inject(BookingService);

  readonly row = input.required<ScheduledClass>();

  /** One spot was released. The screen patches the tile's free-spot count. */
  readonly released = output<void>();
  readonly closed = output<void>();

  protected readonly rows = signal<ClassBooking[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** Booking ids with a release in flight, so one slow row does not disable the rest. */
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  /** The booking whose release failed, and what to say about it. */
  protected readonly failedId = signal<string | null>(null);
  protected readonly failure = signal<string | null>(null);

  ngOnInit(): void {
    // Not the constructor: a required signal input is not readable until the binding is set.
    void this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      this.rows.set(await this.bookings.getForClass(this.row().id));
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Releases somebody's spot.
   *
   * The row is removed rather than refetched — the response already said the spot is gone — and the
   * screen is told, so the tile behind this overlay stops disagreeing with the list in front of it.
   */
  protected async release(booking: ClassBooking): Promise<void> {
    this.setBusy(booking.bookingId, true);
    this.failedId.set(null);
    this.failure.set(null);

    try {
      await this.bookings.cancelAsAdmin(this.row().id, booking.bookingId);

      this.rows.update((rows) =>
        rows.filter((candidate) => candidate.bookingId !== booking.bookingId),
      );

      this.released.emit();
    } catch (error) {
      const reason = ((error as HttpErrorResponse)?.error as { reason?: string } | undefined)
        ?.reason;

      // Kept on the ROW rather than raised to the screen: the refusal is about one person's spot,
      // and the admin is looking straight at it.
      this.failedId.set(booking.bookingId);
      this.failure.set(bookingFailureMessage(reason));
    } finally {
      this.setBusy(booking.bookingId, false);
    }
  }

  protected isBusy(bookingId: string): boolean {
    return this.busy().has(bookingId);
  }

  protected close(): void {
    this.closed.emit();
  }

  private setBusy(bookingId: string, value: boolean): void {
    this.busy.update((ids) => {
      const next = new Set(ids);
      if (value) {
        next.add(bookingId);
      } else {
        next.delete(bookingId);
      }
      return next;
    });
  }
}
