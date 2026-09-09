import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BookingService } from '../../core/scheduling/booking.service';
import { MyBooking } from '../../core/scheduling/booking.models';
import { ClassSummary } from '../../shared/class-summary/class-summary';

/**
 * The member's upcoming bookings (prd.md FR-010).
 *
 * A LIST, DELIBERATELY NOT A CALENDAR. The schedule answers "what is on"; this answers "what am I
 * committed to", which is a short chronological list and reads worse as a grid. It also must not
 * import the calendar at all: `/schedule` and `/admin/classes` are lazy specifically to keep
 * angular-calendar and date-fns out of the initial bundle, and a third screen pulling them in would
 * undo that from a route that has no use for them.
 *
 * READ-ONLY SINCE S-16 (MP-01). The cancel button is gone, and with it the per-row busy set and
 * failure mapping this screen used to carry: a member does not release their own spot any more, the
 * desk does. What is left is the list itself, which is the half FR-010 always asked for.
 *
 * Shell shape follows `schedule.ts` — rows, loading, loadFailed, a generation fence.
 */
@Component({
  imports: [ClassSummary, RouterLink],
  selector: 'app-my-classes',
  styleUrl: './my-classes.scss',
  templateUrl: './my-classes.html',
})
export class MyClasses implements OnInit {
  private readonly bookings = inject(BookingService);

  protected readonly rows = signal<MyBooking[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

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
}
