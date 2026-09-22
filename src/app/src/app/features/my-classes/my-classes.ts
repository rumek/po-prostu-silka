import { Component, OnInit, inject, signal } from '@angular/core';
import { BookingService } from '../../core/scheduling/booking.service';
import { MyBooking } from '../../core/scheduling/booking.models';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import { ClassSummary } from '../../shared/class-summary/class-summary';
import { createLoadFence } from '../../shared/forms/load-fence';
import { Loading } from '../../shared/forms/loading/loading';
import { Empty } from '../../shared/forms/empty/empty';
import { Row } from '../../shared/list/row';

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
  imports: [Row, Empty, Loading, ClassSummary],
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
   * WHY the load failed, when the transport can say (S-19).
   *
   * The screen state itself is unchanged — this is still outlet 4, and the sentence in the template
   * still names what could not be fetched. What is new is the second half: a dead network now says
   * so, where before a killed API read exactly like a server that answered and refused.
   */
  protected readonly loadMessage = signal<string | null>(null);

  /**
   * Same fence as `schedule.ts`. There is no navigation here, but a reload racing a first load is
   * still two responses that can land in either order.
   */
  private readonly fence = createLoadFence();

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const rows = await this.bookings.getMine();

      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.rows.set(rows);
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
}
