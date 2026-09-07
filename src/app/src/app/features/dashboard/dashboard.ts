import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { MemberAdminService } from '../../core/admin/member-admin.service';
import { BookingService } from '../../core/scheduling/booking.service';
import { ClassService } from '../../core/scheduling/class.service';
import { MyBooking } from '../../core/scheduling/booking.models';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { TrainingPlanService } from '../../core/training/training-plan.service';
import { TrainingPlanDetail } from '../../core/training/training-plan.models';
import { ClassSummary } from '../../shared/class-summary/class-summary';
import { PlanSummary } from '../../shared/plan-summary/plan-summary';

/** How many upcoming bookings the member's card shows before deferring to /my-classes (FR-023). */
const NEAREST_CLASSES = 3;

/** Days past today the admin's "upcoming" card looks ahead. Well inside the API's 62-day cap. */
const UPCOMING_DAYS = 7;

/**
 * The landing screen for every approved account (prd.md FR-023, FR-024).
 *
 * ONE ROUTE, TWO AUDIENCES. `/` carries authGuard + activeMemberGuard, which admit member, trainer
 * and admin alike, and adminGuard sends a rebuffed admin here — so this is where an admin lands
 * whatever else happens. The admin section is therefore a branch inside the component rather than a
 * route of its own, exactly as app.html branches its links. An admin is also a member who books
 * classes, and sees both halves.
 *
 * EVERY CARD LOADS ON ITS OWN. Four independent requests with four independent states, because a
 * dashboard that blanks itself when one of them fails is worse than one that shows three cards and an
 * error. This is why there is no single `loading` flag here.
 *
 * READ-ONLY. Nothing is cancelled or approved from this screen; each card links to the one that owns
 * the action. Adding an action here means adding per-row busy state and failure mapping to a screen
 * whose whole job is to be glanceable.
 *
 * It must not import date-fns — see `todayWindow()`.
 */
@Component({
  imports: [ClassSummary, PlanSummary, RouterLink],
  selector: 'app-dashboard',
  styleUrl: './dashboard.scss',
  templateUrl: './dashboard.html',
})
export class Dashboard implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly bookings = inject(BookingService);
  private readonly plans = inject(TrainingPlanService);
  private readonly members = inject(MemberAdminService);
  private readonly classes = inject(ClassService);

  protected readonly displayName = computed(() => this.auth.user()?.displayName ?? null);
  protected readonly isAdmin = computed(() => this.auth.isAdmin());

  // --- Member: nearest classes -------------------------------------------------------------------

  private readonly allBookings = signal<MyBooking[]>([]);
  protected readonly bookingsLoading = signal(true);
  protected readonly bookingsFailed = signal(false);

  /**
   * One fence PER CARD, not one for the screen — the same guard `my-classes.ts` and `schedule.ts`
   * carry, for the same reason: two responses to the same card can land in either order, and the
   * loser must not write back. A single shared counter would be wrong here, because a retry on one
   * card would then discard an in-flight response belonging to another.
   */
  private bookingsGeneration = 0;

  /**
   * Sliced here, not sorted here. The API already orders by the class's start
   * (BookingQuery.GetUpcomingForMemberAsync), and re-sorting would be a second source of truth for a
   * rule the server already owns.
   */
  protected readonly nearestBookings = computed(() => this.allBookings().slice(0, NEAREST_CLASSES));
  protected readonly hasMoreBookings = computed(() => this.allBookings().length > NEAREST_CLASSES);

  // --- Member: active plan -----------------------------------------------------------------------

  /** Null means "no plan assigned" ONLY when planLoading and planFailed are both false. */
  protected readonly plan = signal<TrainingPlanDetail | null>(null);
  protected readonly planLoading = signal(true);
  protected readonly planFailed = signal(false);
  private planGeneration = 0;

  // --- Admin: pending approvals ------------------------------------------------------------------

  protected readonly pendingCount = signal(0);
  protected readonly pendingLoading = signal(true);
  protected readonly pendingFailed = signal(false);
  private pendingGeneration = 0;

  // --- Admin: today and upcoming -----------------------------------------------------------------

  protected readonly todayClasses = signal<ScheduledClass[]>([]);
  protected readonly upcomingClasses = signal<ScheduledClass[]>([]);
  protected readonly classesLoading = signal(true);
  protected readonly classesFailed = signal(false);
  private classesGeneration = 0;

  ngOnInit(): void {
    void this.loadBookings();
    void this.loadPlan();

    // Guarded, not merely hidden: these two endpoints answer 403 to a non-admin, and firing them for
    // every member would put two guaranteed failures in the console on every visit to the home screen.
    if (this.isAdmin()) {
      void this.loadPending();
      void this.loadClasses();
    }
  }

  protected async loadBookings(): Promise<void> {
    const generation = ++this.bookingsGeneration;

    this.bookingsLoading.set(true);
    this.bookingsFailed.set(false);

    try {
      const rows = await this.bookings.getMine();

      if (generation !== this.bookingsGeneration) {
        return;
      }

      this.allBookings.set(rows);
    } catch {
      if (generation !== this.bookingsGeneration) {
        return;
      }

      this.allBookings.set([]);
      this.bookingsFailed.set(true);
    } finally {
      if (generation === this.bookingsGeneration) {
        this.bookingsLoading.set(false);
      }
    }
  }

  protected async loadPlan(): Promise<void> {
    const generation = ++this.planGeneration;

    this.planLoading.set(true);
    this.planFailed.set(false);

    try {
      const plan = await this.plans.getMine();

      if (generation !== this.planGeneration) {
        return;
      }

      this.plan.set(plan);
    } catch {
      if (generation !== this.planGeneration) {
        return;
      }

      // Cleared as well as flagged, following my-plan.ts: a stale plan under an error banner invites
      // the member to act on something the app no longer believes it has.
      this.plan.set(null);
      this.planFailed.set(true);
    } finally {
      if (generation === this.planGeneration) {
        this.planLoading.set(false);
      }
    }
  }

  protected async loadPending(): Promise<void> {
    const generation = ++this.pendingGeneration;

    this.pendingLoading.set(true);
    this.pendingFailed.set(false);

    try {
      const pending = await this.members.getPending();

      if (generation !== this.pendingGeneration) {
        return;
      }

      this.pendingCount.set(pending.length);
    } catch {
      if (generation !== this.pendingGeneration) {
        return;
      }

      this.pendingCount.set(0);
      this.pendingFailed.set(true);
    } finally {
      if (generation === this.pendingGeneration) {
        this.pendingLoading.set(false);
      }
    }
  }

  /**
   * ONE request, bucketed here, for both admin class cards.
   *
   * The window MUST start at local midnight. Called with no bounds this endpoint defaults to
   * `[now, now + 62d)` — which silently drops the classes that started earlier today, and those are
   * precisely the ones an admin running the day is looking for.
   */
  protected async loadClasses(): Promise<void> {
    const generation = ++this.classesGeneration;

    this.classesLoading.set(true);
    this.classesFailed.set(false);

    const { from, to, tomorrow } = this.todayWindow();

    try {
      const rows = await this.classes.getAdminClasses(from, to);

      if (generation !== this.classesGeneration) {
        return;
      }

      this.todayClasses.set(rows.filter((row) => new Date(row.startsAt) < tomorrow));
      this.upcomingClasses.set(rows.filter((row) => new Date(row.startsAt) >= tomorrow));
    } catch {
      if (generation !== this.classesGeneration) {
        return;
      }

      this.todayClasses.set([]);
      this.upcomingClasses.set([]);
      this.classesFailed.set(true);
    } finally {
      if (generation === this.classesGeneration) {
        this.classesLoading.set(false);
      }
    }
  }

  /**
   * Local midnight today, local midnight tomorrow, and the far edge of the upcoming window.
   *
   * NATIVE DATE ARITHMETIC ON PURPOSE. date-fns has startOfDay and addDays, and this screen may not
   * use them: it is an EAGER route, and date-fns currently reaches the bundle only through the lazy
   * calendar chunk that app.routes.ts went out of its way to keep separate. `setDate` past the end of
   * the month rolls the month over on its own, so there is no arithmetic to get wrong here.
   */
  private todayWindow(): { from: Date; to: Date; tomorrow: Date } {
    const from = new Date();
    from.setHours(0, 0, 0, 0);

    const tomorrow = new Date(from);
    tomorrow.setDate(tomorrow.getDate() + 1);

    const to = new Date(from);
    to.setDate(to.getDate() + UPCOMING_DAYS + 1);

    return { from, to, tomorrow };
  }
}
