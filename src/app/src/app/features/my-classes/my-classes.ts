import { Component, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';
import { BookingService } from '../../core/scheduling/booking.service';
import { MyBooking } from '../../core/scheduling/booking.models';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import { ClassSummary } from '../../shared/class-summary/class-summary';
import { createLoadFence } from '../../shared/forms/load-fence';
import { Loading } from '../../shared/forms/loading/loading';
import { Empty } from '../../shared/forms/empty/empty';
import { Row } from '../../shared/list/row';
import { AttendanceHistory } from './attendance-history';

/** The query param that selects the history tab, and its one value. Absent means upcoming. */
export const VIEW_PARAM = 'widok';
export const HISTORY_VIEW = 'historia';

export type MyClassesView = 'upcoming' | 'history';

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
 *
 * <h2>Two tabs since S-27: Nadchodzące | Historia</h2>
 *
 * The tab lives in the URL (`?widok=historia`), so a reload keeps it — and it REPLACES the history
 * entry rather than pushing one, so system back leaves the screen, as a tab switch does on Android.
 * The screen's identity is untouched: its title is still "Zajęcia", its `h1` still "Moje zajęcia".
 * The history is rendered only once its tab is first selected, and fetches its own data then; after
 * that it stays mounted (hidden), so flipping back and forth does not refetch.
 */
@Component({
  imports: [Row, Empty, Loading, ClassSummary, AttendanceHistory],
  selector: 'app-my-classes',
  styleUrl: './my-classes.scss',
  templateUrl: './my-classes.html',
})
export class MyClasses implements OnInit {
  private readonly bookings = inject(BookingService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Which tab the URL selects. */
  protected readonly view = toSignal(
    this.route.queryParamMap.pipe(
      map((params): MyClassesView =>
        params.get(VIEW_PARAM) === HISTORY_VIEW ? 'history' : 'upcoming',
      ),
    ),
    { initialValue: 'upcoming' as MyClassesView },
  );

  /** Latches on the first time the history tab is shown — what makes the history lazy. */
  private readonly historyOpened = signal(false);

  protected readonly renderHistory = computed(
    () => this.historyOpened() || this.view() === 'history',
  );

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

  /** Selects a tab by rewriting the URL in place — no new history entry. */
  protected select(view: MyClassesView): void {
    if (view === 'history') {
      this.historyOpened.set(true);
    }

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { [VIEW_PARAM]: view === 'history' ? HISTORY_VIEW : null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  /**
   * The WAI-ARIA tabs keyboard model: arrows move between the two tabs (wrapping), Home and End jump
   * to the ends, and the tab that receives focus is selected with it.
   */
  protected onTabKeydown(event: KeyboardEvent): void {
    const order: MyClassesView[] = ['upcoming', 'history'];
    const current = order.indexOf(this.view());

    let next: number;
    switch (event.key) {
      case 'ArrowRight':
        next = (current + 1) % order.length;
        break;
      case 'ArrowLeft':
        next = (current - 1 + order.length) % order.length;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = order.length - 1;
        break;
      default:
        return;
    }

    event.preventDefault();
    this.select(order[next]);
    this.host.nativeElement.querySelector<HTMLElement>(`#tab-${order[next]}`)?.focus();
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
