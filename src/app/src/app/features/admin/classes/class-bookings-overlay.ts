import { DatePipe } from '@angular/common';
import {
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Member } from '../../../core/admin/member-admin.models';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import { bookingFailureMessage } from '../../../core/scheduling/booking-failure';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { BookingService } from '../../../core/scheduling/booking.service';
import { ClassBooking } from '../../../core/scheduling/booking.models';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { createBusySet } from '../../../shared/forms/busy-set';
import { createLoadFence } from '../../../shared/forms/load-fence';
import { useOverlayFocus } from '../../../shared/forms/overlay-focus';
import { Field } from '../../../shared/forms/field/field';
import { Select } from '../../../shared/forms/select/select';
import { Loading } from '../../../shared/forms/loading/loading';
import { Empty } from '../../../shared/forms/empty/empty';
import { Row } from '../../../shared/list/row';

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
 *
 * <h2>It also signs people up (S-14, AM-007)</h2>
 *
 * A member with no account cannot tap Book, so without this the club could record them, plan for
 * them, and never get them into a class. The picker offers the active members matching a phrase who
 * are not already on the list.
 *
 * A SEARCH, NOT THE CLUB (S-21). It used to load every active member into the select on open — the
 * member list's second "fetch everyone" — which at hundreds of members is a select nobody can scroll
 * and a request nobody needed. It asks the paged endpoint for {@link PICKER_RESULTS} matches once
 * typing pauses, like the members screen does.
 */
/** How many matches the picker offers. Past this, it asks the admin to narrow the phrase. */
export const PICKER_RESULTS = 20;

/** Same pause as the members screen's search box — one request per pause, not per key. */
export const PICKER_DEBOUNCE_MS = 300;

@Component({
  // On the host, not on the panel: Escape has to close the overlay wherever focus is, including
  // before the admin has touched anything.
  host: { '(document:keydown.escape)': 'close()' },
  imports: [Row, Empty, Loading, Select, Field, DatePipe, FormsModule],
  selector: 'app-class-bookings-overlay',
  styleUrl: './class-bookings-overlay.scss',
  templateUrl: './class-bookings-overlay.html',
})
export class ClassBookingsOverlay implements OnInit {
  // Focus enters the panel on open and returns to whatever opened it on close — the half of
  // `role="dialog" aria-modal="true"` these three overlays declared and never did (S-19).
  private readonly focus = useOverlayFocus();

  private readonly bookings = inject(BookingService);
  private readonly members = inject(MemberAdminService);

  readonly row = input.required<ScheduledClass>();

  /** One spot was released. The screen patches the tile's free-spot count. */
  readonly released = output<void>();

  /**
   * Somebody was signed up. Carries the CLASS the API answered with rather than a void signal like
   * `released`, because the admin path can refuse for reasons that make the tile's count wrong by
   * more than one — the server's number is the only one worth trusting.
   */
  readonly booked = output<ScheduledClass>();
  readonly closed = output<void>();

  protected readonly rows = signal<ClassBooking[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** Rows with a mutation in flight, so one slow row does not disable the whole list. */
  protected readonly busy = createBusySet();

  /** The booking whose release failed, and what to say about it. */
  protected readonly failedId = signal<string | null>(null);
  protected readonly failure = signal<string | null>(null);

  // --- signing somebody up (S-14) -------------------------------------------

  /** What is in the picker's search box. */
  protected readonly addSearch = signal('');

  /**
   * The phrase the current matches answer, or empty when nothing has been searched. Separate from
   * `addSearch`, which runs ahead of it by one debounce pause — "nobody matches" must describe the
   * phrase that was actually asked, not the one still being typed.
   */
  protected readonly searched = signal('');

  /** The active members matching `searched`, with or without a login — at most PICKER_RESULTS. */
  protected readonly candidates = signal<Member[]>([]);

  /** How many match in all, so the picker can say when it is showing only some of them. */
  protected readonly candidatesTotal = signal(0);

  /** Its own fence: the roster and the search are independent loads (AGENTS.md, load-fence.ts). */
  private readonly searchFence = createLoadFence();
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** The picker's value. Empty string is "nobody chosen", which is what the placeholder option is. */
  protected readonly chosen = signal('');

  protected readonly adding = signal(false);
  protected readonly addFailure = signal<string | null>(null);

  /**
   * Who the picker offers: the matches not already holding a spot.
   *
   * Filtering the roster out CLIENT-SIDE is deliberate — the server would refuse them with
   * `already_booked` anyway, and this way the select holds nothing that cannot be chosen. It is a
   * filter over at most PICKER_RESULTS rows, so a page of matches that were all booked already reads
   * as "nobody matching can be added" rather than as a short list.
   */
  protected readonly bookable = computed(() => {
    const taken = new Set(this.rows().map((booking) => booking.memberId));

    return this.candidates().filter((member) => !taken.has(member.id));
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.cancelSearch());
  }

  ngOnInit(): void {
    // Not the constructor: a required signal input is not readable until the binding is set.
    void this.load();
  }

  /** The picker's every keystroke. Searches once typing pauses; a blank phrase clears at once. */
  protected onAddSearchInput(value: string): void {
    this.addSearch.set(value);
    this.cancelSearch();

    const phrase = value.trim();
    if (!phrase) {
      this.clearCandidates();
      return;
    }

    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      void this.searchCandidates(phrase);
    }, PICKER_DEBOUNCE_MS);
  }

  /**
   * Active members matching the phrase. A failure is deliberately QUIET, as the whole-club load it
   * replaced was: the picker offers nobody, and the panel's real job — showing who is coming — is
   * unaffected.
   */
  private async searchCandidates(phrase: string): Promise<void> {
    const generation = this.searchFence.begin();

    try {
      const page = await this.members.getMembers({
        filter: 'Active',
        search: phrase,
        pageSize: PICKER_RESULTS,
      });

      if (!this.searchFence.isCurrent(generation)) {
        return;
      }

      this.candidates.set(page.items);
      this.candidatesTotal.set(page.total);
      this.searched.set(phrase);

      // A choice the new matches no longer offer would submit a person the admin can no longer see.
      if (!this.bookable().some((candidate) => candidate.id === this.chosen())) {
        this.chosen.set('');
      }
    } catch {
      if (this.searchFence.isCurrent(generation)) {
        this.clearCandidates();
      }
    }
  }

  /** Drops the matches — and, through the fence, any search still in flight. */
  private clearCandidates(): void {
    this.searchFence.begin();
    this.candidates.set([]);
    this.candidatesTotal.set(0);
    this.searched.set('');
    this.chosen.set('');
  }

  private cancelSearch(): void {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }

  /**
   * Signs the chosen member up.
   *
   * The roster is refetched rather than patched: the API answers with the class, not with the
   * booking, so there is no row here to append — and the response's free-spot count is what the
   * screen behind gets told about.
   */
  protected async add(): Promise<void> {
    const memberId = this.chosen();
    if (!memberId || this.adding()) {
      return;
    }

    this.adding.set(true);
    this.addFailure.set(null);

    try {
      const updated = await this.bookings.bookForMember(this.row().id, memberId);

      // The phrase goes with the choice: the next person the admin adds is a new search, and a
      // stale list of matches would offer the person just added (until the roster reload lands).
      this.cancelSearch();
      this.addSearch.set('');
      this.clearCandidates();
      this.booked.emit(updated);
      await this.load();
    } catch (error) {
      const info = classifyFailure(error);

      // A 404 here is the MEMBER, not the class: the class is on screen. Somebody deleted the record
      // between the picker loading and the admin choosing from it. That used to be an ad-hoc
      // `status === 404` test; it is now a kind, and the sentence comes from transport-messages
      // along with 429, 5xx and offline — which this branch never distinguished at all.
      //
      // ONE TABLE SINCE S-16. It used to need a third-person overlay (adminBookingFailureMessage)
      // because the shared messages addressed the member directly; MP-01 removed the member-facing
      // surfaces, so the shared table is now written in this screen's voice to begin with.
      this.addFailure.set(transportMessage(info) ?? bookingFailureMessage(info.reason));
    } finally {
      this.adding.set(false);
    }
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
    this.busy.setBusy(booking.bookingId, true);
    this.failedId.set(null);
    this.failure.set(null);

    try {
      await this.bookings.cancelAsAdmin(this.row().id, booking.bookingId);

      this.rows.update((rows) =>
        rows.filter((candidate) => candidate.bookingId !== booking.bookingId),
      );

      this.released.emit();
    } catch (error) {
      const info = classifyFailure(error);

      // Kept on the ROW rather than raised to the screen or to a toast: the refusal is about one
      // person's spot, and the admin is looking straight at it.
      this.failedId.set(booking.bookingId);
      this.failure.set(transportMessage(info) ?? bookingFailureMessage(info.reason));
    } finally {
      this.busy.setBusy(booking.bookingId, false);
    }
  }

  protected close(): void {
    this.closed.emit();
  }
}
