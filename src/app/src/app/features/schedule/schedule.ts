import { Component, computed, inject, signal } from '@angular/core';
import { MemberAdminService } from '../../core/admin/member-admin.service';
import { AuthService } from '../../core/auth/auth.service';
import { personaOf } from '../../core/auth/persona';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import {
  BookingCandidateSearch,
  adminCandidateSearch,
  trainerCandidateSearch,
} from '../../core/scheduling/booking-candidates';
import { ClassService } from '../../core/scheduling/class.service';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { TrainingPlanService } from '../../core/training/training-plan.service';
import { CalendarRange, ScheduleCalendar } from '../../shared/calendar/schedule-calendar';
import { createLoadFence } from '../../shared/forms/load-fence';
import { Icon } from '../../shared/icons/icon';
import { ClassBookingsOverlay } from '../class-bookings/class-bookings-overlay';

/**
 * The schedule (prd.md FR-007; prd-v2 FR-015, FR-016, FR-018) — a STAFF screen since S-25.
 *
 * A CALENDAR since S-07: one day at a time on a phone, the whole week from 48rem up, the same shared
 * calendar the admin panel renders.
 *
 * <h2>Who sees what (S-25)</h2>
 *
 * A member has no schedule: staffGuard sends them home, and `GET /api/classes` refuses them. What the
 * endpoint returns is the persona's schedule, decided on the server — every class for an admin, only
 * the classes they instruct for a trainer — so this screen never filters and never asks for the
 * caller's own bookings (staff hold none).
 *
 * <h2>A class opens its roster</h2>
 *
 * Tapping a class opens the bookings overlay the admin calendar uses, so staff can see who is coming,
 * release a spot and sign a member up from here — on a phone too, where the admin calendar refuses to
 * render (UX-01). The API narrows a trainer to their own classes (`MayActOn`); this screen only
 * chooses where the picker searches: the admin member list for an admin, the trainer's member list
 * (name-only, members only) for a trainer.
 */
@Component({
  imports: [ClassBookingsOverlay, Icon, ScheduleCalendar],
  selector: 'app-schedule',
  styleUrl: './schedule.scss',
  templateUrl: './schedule.html',
})
export class Schedule {
  private readonly classes = inject(ClassService);
  private readonly auth = inject(AuthService);

  protected readonly persona = computed(() => personaOf(this.auth.user()));

  /** Built once each; `candidateSearch` picks between them by persona. */
  private readonly adminSearch = adminCandidateSearch(inject(MemberAdminService));
  private readonly trainerSearch = trainerCandidateSearch(inject(TrainingPlanService));

  protected readonly candidateSearch = computed<BookingCandidateSearch>(() =>
    this.persona() === 'admin' ? this.adminSearch : this.trainerSearch,
  );

  protected readonly rows = signal<ScheduledClass[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /**
   * WHY the load failed, when the transport can say (S-19). Still outlet 4 — the calendar states the
   * failure and the screen owns the retry.
   */
  protected readonly loadMessage = signal<string | null>(null);

  /** The class whose roster is open, or null. */
  protected readonly selected = signal<ScheduledClass | null>(null);

  /** The window the calendar is showing. Null until its first emission, which is the first load. */
  private readonly range = signal<CalendarRange | null>(null);

  /**
   * Nothing cancels an in-flight request, so the last RESPONSE would otherwise win: two quick taps on
   * "next week" can land their responses in either order. Same guard as classes.ts.
   */
  private readonly fence = createLoadFence();

  /** Driven by the calendar's rangeChange, which fires on init too — hence no ngOnInit. */
  protected async load(range: CalendarRange): Promise<void> {
    this.range.set(range);

    // A window change invalidates the open overlay: its class may not even be on screen any more.
    this.closeBookings();

    const generation = this.fence.begin();

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const rows = await this.classes.getSchedule(range.from, range.to);

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

  /**
   * Refetches the window on screen. The calendar owns navigation, so there is no gesture that would
   * retry as a side effect — without this the answer to a dropped connection is "reload the page".
   */
  protected async reload(): Promise<void> {
    const range = this.range();

    if (range) {
      await this.load(range);
    }
  }

  protected openBookings(row: ScheduledClass): void {
    this.selected.set(row);
  }

  protected closeBookings(): void {
    this.selected.set(null);
  }

  /** A spot was released — the tile and the overlay's header both show one more free. */
  protected afterRelease(row: ScheduledClass): void {
    const freed = (candidate: ScheduledClass) =>
      candidate.id === row.id ? { ...candidate, freeSpots: candidate.freeSpots + 1 } : candidate;

    this.rows.update((rows) => rows.map(freed));
    this.selected.update((open) => (open ? freed(open) : open));
  }

  /** Somebody was signed up — the server answered with the class as it now stands; trust that. */
  protected afterBooking(updated: ScheduledClass): void {
    this.rows.update((rows) =>
      rows.map((candidate) => (candidate.id === updated.id ? updated : candidate)),
    );
    this.selected.update((open) => (open && open.id === updated.id ? updated : open));
  }
}
