import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Member } from '../../../core/admin/member-admin.models';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import {
  adminBookingFailureMessage,
  bookingFailureMessage,
} from '../../../core/scheduling/booking-failure';
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
 *
 * <h2>It also signs people up (S-14, AM-007)</h2>
 *
 * A member with no account cannot tap Book, so without this the club could record them, plan for
 * them, and never get them into a class. The picker offers everyone who may use the club and is not
 * already on the list, which is why it loads the member list rather than reusing the roster.
 */
@Component({
  // On the host, not on the panel: Escape has to close the overlay wherever focus is, including
  // before the admin has touched anything.
  host: { '(document:keydown.escape)': 'close()' },
  imports: [DatePipe, FormsModule],
  selector: 'app-class-bookings-overlay',
  styleUrl: './class-bookings-overlay.scss',
  templateUrl: './class-bookings-overlay.html',
})
export class ClassBookingsOverlay implements OnInit {
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

  /** Booking ids with a release in flight, so one slow row does not disable the rest. */
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  /** The booking whose release failed, and what to say about it. */
  protected readonly failedId = signal<string | null>(null);
  protected readonly failure = signal<string | null>(null);

  // --- signing somebody up (S-14) -------------------------------------------

  /** Everyone who may use the club, whether or not they have a login. */
  protected readonly candidates = signal<Member[]>([]);

  /** The picker's value. Empty string is "nobody chosen", which is what the placeholder option is. */
  protected readonly chosen = signal('');

  protected readonly adding = signal(false);
  protected readonly addFailure = signal<string | null>(null);

  /**
   * Who the picker offers: active members not already holding a spot.
   *
   * Filtering the roster out CLIENT-SIDE is deliberate — the server would refuse them with
   * `already_booked` anyway, and this way the list the admin scrolls has nothing in it that cannot
   * be chosen. A club's member list fits in memory; this is not a search.
   */
  protected readonly bookable = computed(() => {
    const taken = new Set(this.rows().map((booking) => booking.memberId));

    return this.candidates().filter((member) => !taken.has(member.id));
  });

  ngOnInit(): void {
    // Not the constructor: a required signal input is not readable until the binding is set.
    void this.load();
    void this.loadCandidates();
  }

  /**
   * The member list, loaded once. A failure here is deliberately QUIET: the sign-up picker simply
   * does not appear, and the panel's real job — showing who is coming — is unaffected.
   */
  private async loadCandidates(): Promise<void> {
    try {
      this.candidates.set(await this.members.getMembers('Active'));
    } catch {
      this.candidates.set([]);
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

      this.chosen.set('');
      this.booked.emit(updated);
      await this.load();
    } catch (error) {
      const response = error as HttpErrorResponse;

      // A 404 here is the member, not the class: the class is on screen. Somebody deleted the record
      // between the picker loading and the admin choosing from it.
      this.addFailure.set(
        response?.status === 404
          ? 'Nie znaleziono tej osoby — lista mogła się zmienić.'
          : adminBookingFailureMessage(
              (response?.error as { reason?: string } | undefined)?.reason,
            ),
      );
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
