import { DatePipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MemberAdminService } from '../../core/admin/member-admin.service';
import { AuthService } from '../../core/auth/auth.service';
import { personaOf } from '../../core/auth/persona';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import { useScreenTitle } from '../../core/layout/screen-title';
import { UpLink } from '../../core/layout/up';
import {
  BookingCandidate,
  BookingCandidateSearch,
  adminCandidateSearch,
  trainerCandidateSearch,
} from '../../core/scheduling/booking-candidates';
import { bookingFailureMessage } from '../../core/scheduling/booking-failure';
import { rosterFailureMessage } from '../../core/scheduling/roster-failure';
import {
  BOOKABLE_GAP,
  GroupRosterMember,
  GroupRosterView,
  RosterReport as Report,
} from '../../core/scheduling/roster.models';
import { RosterService } from '../../core/scheduling/roster.service';
import { TrainingPlanService } from '../../core/training/training-plan.service';
import { createBusySet } from '../../shared/forms/busy-set';
import { Empty } from '../../shared/forms/empty/empty';
import { Field } from '../../shared/forms/field/field';
import { createLoadFence } from '../../shared/forms/load-fence';
import { Loading } from '../../shared/forms/loading/loading';
import { Select } from '../../shared/forms/select/select';
import { Icon } from '../../shared/icons/icon';
import { List } from '../../shared/list/list';
import { Row } from '../../shared/list/row';
import { RosterReport } from '../../shared/roster-report/roster-report';
import { RosterLine, groupRosterLines } from '../../shared/roster-report/roster-lines';
import { ToastService } from '../../shared/toast/toast.service';

/** Same pause as the bookings overlay's picker — one request per pause, not per key. */
const SEARCH_DEBOUNCE_MS = 300;

/**
 * A group's fixed roster ("Skład", S-37) — one screen for both staff personas.
 *
 * <h2>Who sees it</h2>
 *
 * Mounted twice, like the plan builder: `/admin/class-groups/:id/roster` behind adminGuard and
 * `/trainer/groups/:id` behind trainerGuard. The API decides what a trainer may manage (a group they
 * instruct an upcoming class of) and answers anything else with a 403, which this screen shows as its
 * state. The only thing that differs by persona is where the picker searches — the admin member list
 * or the trainer's, which already excludes staff.
 *
 * <h2>What it shows</h2>
 *
 * Each member with how many upcoming classes they are booked into, and every class they are missing,
 * grouped by why ("brak karnetu: 4.11, 11.11"). The gaps are derived by the API on every read, so the
 * screen is always current; "Uzupełnij zapisy" retries all of them, and adding a person books them in
 * at once. Whatever could not be booked stays on screen in the report panel until dismissed.
 *
 * <h2>Where a failure goes (S-19)</h2>
 *
 * A failed load is screen state (outlet 4); a refused add, remove or sync is a toast (outlet 3) — the
 * screen stays put, and the words come from the roster table or the transport.
 */
@Component({
  imports: [
    DatePipe,
    Empty,
    Field,
    FormsModule,
    Icon,
    List,
    Loading,
    RosterReport,
    Row,
    Select,
    UpLink,
  ],
  selector: 'app-group-roster',
  styleUrl: './group-roster.scss',
  templateUrl: './group-roster.html',
})
export class GroupRoster implements OnInit, OnDestroy {
  private readonly rosters = inject(RosterService);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  /** Built once each; `search` picks between them by persona, as the schedule screen does. */
  private readonly adminSearch = adminCandidateSearch(inject(MemberAdminService));
  private readonly trainerSearch = trainerCandidateSearch(inject(TrainingPlanService));

  protected readonly search = computed<BookingCandidateSearch>(() =>
    personaOf(this.auth.user()) === 'admin' ? this.adminSearch : this.trainerSearch,
  );

  /** Where "up" goes — the route's screen parent: the admin's group list or the trainer's. */
  protected readonly parentLink: string =
    (this.route.snapshot.data['parent'] as string | undefined) ?? '/';

  private groupId = '';

  protected readonly view = signal<GroupRosterView | null>(null);
  protected readonly loading = signal(true);

  /** Why the screen could not be populated — a 403 or 404 says so in its own words. */
  protected readonly loadMessage = signal<string | null>(null);
  private readonly fence = createLoadFence();

  /** The last report with something skipped; cleared on dismiss or on the next action. */
  protected readonly report = signal<Report | null>(null);

  protected readonly screenTitleEffect = useScreenTitle(() => {
    const view = this.view();
    return view ? `Skład — ${view.name}` : null;
  });

  protected readonly full = computed(() => {
    const view = this.view();
    return view !== null && view.members.length >= view.capacity;
  });

  // --- the picker: the bookings overlay's shape (search box, then a select of its matches) ---------

  protected readonly addSearch = signal('');
  protected readonly searched = signal('');
  protected readonly candidates = signal<BookingCandidate[]>([]);
  protected readonly candidatesTotal = signal(0);
  protected readonly chosen = signal('');
  protected readonly adding = signal(false);
  private readonly searchFence = createLoadFence();
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Matches not already in the roster. */
  protected readonly addable = computed(() => {
    const taken = new Set(this.view()?.members.map((member) => member.memberId) ?? []);
    return this.candidates().filter((candidate) => !taken.has(candidate.id));
  });

  // --- row actions ----------------------------------------------------------------------------------

  protected readonly syncing = signal(false);
  protected readonly busy = createBusySet();

  /** The member whose removal is awaiting its inline confirmation. */
  protected readonly confirmingRemoval = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    this.groupId = this.route.snapshot.paramMap.get('id') ?? '';
    await this.load();
  }

  ngOnDestroy(): void {
    this.cancelSearchTimer();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();
    this.loading.set(true);
    this.loadMessage.set(null);

    try {
      const view = await this.rosters.get(this.groupId);
      if (this.fence.isCurrent(generation)) {
        this.view.set(view);
      }
    } catch (failure) {
      if (this.fence.isCurrent(generation)) {
        this.loadMessage.set(
          transportMessage(classifyFailure(failure)) ?? 'Nie udało się wczytać składu.',
        );
      }
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.loading.set(false);
      }
    }
  }

  /** A member's gaps, one line per reason with its dates. */
  protected gapLines(member: GroupRosterMember): RosterLine[] {
    return groupRosterLines(
      member.gaps.map((gap) => ({
        memberId: member.memberId,
        memberName: member.displayName,
        reason: gap.reason,
        startsAt: gap.startsAt,
      })),
    );
  }

  /** The words for a gap: the booking table's, or the roster's own "bookable" phrase. */
  protected gapReason(reason: string): string {
    return reason === BOOKABLE_GAP
      ? 'można zapisać — uzupełnij zapisy'
      : bookingFailureMessage(reason);
  }

  protected onAddSearchInput(value: string): void {
    this.addSearch.set(value);
    this.chosen.set('');
    this.cancelSearchTimer();

    const phrase = value.trim();
    if (!phrase) {
      this.clearCandidates();
      return;
    }

    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      void this.searchCandidates(phrase);
    }, SEARCH_DEBOUNCE_MS);
  }

  /** Quiet on failure, like the overlay's: the box simply offers nobody, and typing again retries. */
  private async searchCandidates(phrase: string): Promise<void> {
    const generation = this.searchFence.begin();

    try {
      const page = await this.search().find(phrase);
      if (!this.searchFence.isCurrent(generation)) {
        return;
      }
      this.candidates.set(page.items);
      this.candidatesTotal.set(page.total);
      this.searched.set(phrase);
    } catch {
      if (this.searchFence.isCurrent(generation)) {
        this.clearCandidates();
      }
    }
  }

  private clearCandidates(): void {
    this.searchFence.begin();
    this.candidates.set([]);
    this.candidatesTotal.set(0);
    this.searched.set('');
  }

  private cancelSearchTimer(): void {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }

  protected async add(): Promise<void> {
    const memberId = this.chosen();
    if (!memberId) {
      return;
    }

    this.adding.set(true);
    this.report.set(null);

    try {
      const change = await this.rosters.add(this.groupId, memberId);
      this.view.set(change.roster);
      this.showReport(change.report);

      this.addSearch.set('');
      this.chosen.set('');
      this.clearCandidates();

      this.toast.success(`Dodano do składu. ${this.bookedSentence(change.report.booked)}`);
    } catch (failure) {
      this.toast.error(this.messageFor(failure));
    } finally {
      this.adding.set(false);
    }
  }

  protected async sync(): Promise<void> {
    this.syncing.set(true);
    this.report.set(null);

    try {
      const change = await this.rosters.sync(this.groupId);
      this.view.set(change.roster);
      this.showReport(change.report);

      if (change.report.booked > 0) {
        this.toast.success(this.bookedSentence(change.report.booked));
      } else {
        this.toast.info('Nie było kogo dopisać.');
      }
    } catch (failure) {
      this.toast.error(this.messageFor(failure));
    } finally {
      this.syncing.set(false);
    }
  }

  protected askRemoval(member: GroupRosterMember): void {
    this.confirmingRemoval.set(member.memberId);
  }

  protected cancelRemoval(): void {
    this.confirmingRemoval.set(null);
  }

  protected async remove(member: GroupRosterMember): Promise<void> {
    this.busy.setBusy(member.memberId, true);
    this.report.set(null);

    try {
      this.view.set(await this.rosters.remove(this.groupId, member.memberId));
      this.confirmingRemoval.set(null);
      this.toast.success(`${member.displayName} nie jest już w składzie.`);
    } catch (failure) {
      this.toast.error(this.messageFor(failure));
    } finally {
      this.busy.setBusy(member.memberId, false);
    }
  }

  protected dismissReport(): void {
    this.report.set(null);
  }

  private showReport(report: Report): void {
    this.report.set(report.skipped.length > 0 ? report : null);
  }

  private bookedSentence(booked: number): string {
    if (booked === 0) {
      return 'Nie dopisano na żadne zajęcia.';
    }
    // "zajęcia" is plural-only: 1–4 (and 22–24, …) take "zajęcia", the rest "zajęć".
    const lastDigit = booked % 10;
    const teen = booked % 100 >= 12 && booked % 100 <= 14;
    const word = booked === 1 || (lastDigit >= 2 && lastDigit <= 4 && !teen) ? 'zajęcia' : 'zajęć';
    return `Dopisano na ${booked} ${word}.`;
  }

  private messageFor(failure: unknown): string {
    const info = classifyFailure(failure);
    return transportMessage(info) ?? rosterFailureMessage(info.reason);
  }
}
