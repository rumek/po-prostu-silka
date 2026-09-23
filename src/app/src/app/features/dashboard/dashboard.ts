import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { isStaff, personaOf } from '../../core/auth/persona';
import { DESK_MEDIA_QUERY } from '../../core/layout/breakpoints';
import { mediaQuerySignal } from '../../core/layout/media-query';
import { navigationFor } from '../../core/layout/navigation';
import { MemberAdminService } from '../../core/admin/member-admin.service';
import { BookingService } from '../../core/scheduling/booking.service';
import { ClassService } from '../../core/scheduling/class.service';
import { MyBooking } from '../../core/scheduling/booking.models';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { MembershipPassView } from '../../core/admin/member-admin.models';
import { TrainingPlanService } from '../../core/training/training-plan.service';
import { TrainingPlanDetail } from '../../core/training/training-plan.models';
import { ClassSummary } from '../../shared/class-summary/class-summary';
import { PlanSummary } from '../../shared/plan-summary/plan-summary';
import { createLoadFence } from '../../shared/forms/load-fence';
import { Loading } from '../../shared/forms/loading/loading';
import { Empty } from '../../shared/forms/empty/empty';

/** How many upcoming bookings the member's card shows before deferring to /my-classes (FR-023). */
const NEAREST_CLASSES = 3;

/** Days past today the staff "upcoming" card looks ahead. Well inside the API's 62-day cap. */
const UPCOMING_DAYS = 7;

/**
 * The landing screen for every approved account (prd.md FR-023, FR-024).
 *
 * ONE ROUTE, TWO AUDIENCES (S-25). `/` carries authGuard + activeMemberGuard, which admit every
 * persona, and every persona guard sends a rebuffed user here — so this is where everyone lands. It
 * branches on the persona, and the halves no longer overlap:
 *
 * - a MEMBER gets their nearest bookings, their karnet and their plan;
 * - STAFF (trainer or admin) get "Twoje zajęcia" — the classes they instruct, today and the next
 *   week — and nothing of the member's: staff hold no bookings, karnet or plan.
 *
 * NOT FIRED, NOT MERELY HIDDEN. Staff never request the `/mine` routes (MemberOnly would refuse them) and
 * a member never requests the feed (TrainerOrAdmin would refuse them); the spec pins both with
 * expectNone.
 *
 * EVERY CARD LOADS ON ITS OWN. Four independent requests with four independent states, because a
 * dashboard that blanks itself when one of them fails is worse than one that shows three cards and
 * an error. This is why there is no single `loading` flag here.
 *
 * READ-ONLY. Nothing is cancelled or approved from this screen; each card links to the one that owns
 * the action. Adding an action here means adding per-row busy state and failure mapping to a screen
 * whose whole job is to be glanceable.
 *
 * It must not import date-fns — see `todayWindow()`.
 */
@Component({
  imports: [Empty, Loading, ClassSummary, DatePipe, PlanSummary, RouterLink],
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

  protected readonly displayName = computed(
    () => this.auth.user()?.displayName?.trim().split(/\s+/)[0] ?? null,
  );
  protected readonly persona = computed(() => personaOf(this.auth.user()));
  protected readonly isMember = computed(() => this.persona() === 'member');
  protected readonly isStaff = computed(() => isStaff(this.persona()));

  private readonly desk = mediaQuerySignal(DESK_MEDIA_QUERY, true);

  /**
   * Where "Zobacz grafik" goes — the persona's own Grafik from the navigation table, so the admin's
   * follows the desk boundary exactly as the menu's does.
   */
  protected readonly scheduleLink = computed(
    () =>
      navigationFor(this.persona(), this.desk()).header.find((link) => link.label === 'Grafik')
        ?.route ?? '/schedule',
  );

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
  private readonly bookingsFence = createLoadFence();

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
  private readonly planFence = createLoadFence();

  // --- Member: karnet (S-16, MP-07) --------------------------------------------------------------

  /**
   * The karnet covering TODAY, or null when there is none.
   *
   * Null means "no valid karnet" ONLY when passLoading and passFailed are both false — the same
   * three-signal shape the plan card uses, and for the same reason: "you hold nothing" and "we could
   * not find out" are different things to put in front of a member.
   */
  protected readonly pass = signal<MembershipPassView | null>(null);
  protected readonly passLoading = signal(true);
  protected readonly passFailed = signal(false);
  private readonly passFence = createLoadFence();

  // --- Staff: the classes I instruct, today and upcoming (S-25) ----------------------------------

  protected readonly todayClasses = signal<ScheduledClass[]>([]);
  protected readonly upcomingClasses = signal<ScheduledClass[]>([]);
  protected readonly classesLoading = signal(true);
  protected readonly classesFailed = signal(false);
  private readonly classesFence = createLoadFence();

  ngOnInit(): void {
    // Each branch fires only what its persona's API policy admits — see the class comment.
    if (this.isMember()) {
      void this.loadBookings();
      void this.loadPlan();

      // In PARALLEL with the two above, not after them — this route is eager and every serialised
      // request here is latency every member pays on every visit.
      void this.loadPass();
    } else if (this.isStaff()) {
      void this.loadClasses();
    }
  }

  protected async loadBookings(): Promise<void> {
    const generation = this.bookingsFence.begin();

    this.bookingsLoading.set(true);
    this.bookingsFailed.set(false);

    try {
      const rows = await this.bookings.getMine();

      if (!this.bookingsFence.isCurrent(generation)) {
        return;
      }

      this.allBookings.set(rows);
    } catch {
      if (!this.bookingsFence.isCurrent(generation)) {
        return;
      }

      this.allBookings.set([]);
      this.bookingsFailed.set(true);
    } finally {
      if (this.bookingsFence.isCurrent(generation)) {
        this.bookingsLoading.set(false);
      }
    }
  }

  protected async loadPlan(): Promise<void> {
    const generation = this.planFence.begin();

    this.planLoading.set(true);
    this.planFailed.set(false);

    try {
      const plan = await this.plans.getMine();

      if (!this.planFence.isCurrent(generation)) {
        return;
      }

      this.plan.set(plan);
    } catch {
      if (!this.planFence.isCurrent(generation)) {
        return;
      }

      // Cleared as well as flagged, following my-plan.ts: a stale plan under an error banner invites
      // the member to act on something the app no longer believes it has.
      this.plan.set(null);
      this.planFailed.set(true);
    } finally {
      if (this.planFence.isCurrent(generation)) {
        this.planLoading.set(false);
      }
    }
  }

  protected async loadPass(): Promise<void> {
    const generation = this.passFence.begin();

    this.passLoading.set(true);
    this.passFailed.set(false);

    try {
      const pass = await this.members.getMyPass();

      if (!this.passFence.isCurrent(generation)) {
        return;
      }

      this.pass.set(pass);
    } catch {
      if (!this.passFence.isCurrent(generation)) {
        return;
      }

      // Cleared as well as flagged, following the plan card: a stale karnet under an error banner
      // tells the member they may train when the app no longer knows whether they may.
      this.pass.set(null);
      this.passFailed.set(true);
    } finally {
      if (this.passFence.isCurrent(generation)) {
        this.passLoading.set(false);
      }
    }
  }

  /**
   * ONE request, bucketed here, for both staff class cards: the classes the caller instructs.
   *
   * The window MUST start at local midnight. Called with no bounds the endpoint defaults to a window
   * starting NOW — which silently drops the classes that started earlier today, and those are
   * precisely the ones a trainer running the day is looking for.
   */
  protected async loadClasses(): Promise<void> {
    const generation = this.classesFence.begin();

    this.classesLoading.set(true);
    this.classesFailed.set(false);

    const { from, to, tomorrow } = this.todayWindow();

    try {
      const rows = await this.classes.getInstructedClasses(from, to);

      if (!this.classesFence.isCurrent(generation)) {
        return;
      }

      this.todayClasses.set(rows.filter((row) => new Date(row.startsAt) < tomorrow));
      this.upcomingClasses.set(rows.filter((row) => new Date(row.startsAt) >= tomorrow));
    } catch {
      if (!this.classesFence.isCurrent(generation)) {
        return;
      }

      this.todayClasses.set([]);
      this.upcomingClasses.set([]);
      this.classesFailed.set(true);
    } finally {
      if (this.classesFence.isCurrent(generation)) {
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
