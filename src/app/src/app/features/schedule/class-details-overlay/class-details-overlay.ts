import { DatePipe } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { ScheduledClass } from '../../../core/scheduling/class.models';

/**
 * One class, with the member's booking action (prd.md US-01, FR-008, FR-009).
 *
 * Adapted from `features/admin/classes/class-create-overlay` — the same overlay-over-the-calendar
 * structure, the same backdrop-button and Escape handling, the same styling approach. It is the
 * member's counterpart to that screen's create overlay, not an invention.
 *
 * <h2>The first place a class type's description is ever shown</h2>
 *
 * S-05 gave class types a description and nothing has rendered it since: the admin's list shows
 * names, and a calendar tile has no room. This is where it lands, and it is the reason the member
 * opens a class at all rather than booking straight from the tile.
 *
 * <h2>It owns no state and performs no request</h2>
 *
 * Booking is the SCREEN's job: only the screen can apply the returned class back into the week it is
 * showing, and only the screen knows whether a week navigation has invalidated the result. This
 * component reports two intentions and renders what it is told — which is also what makes it
 * testable without HTTP.
 */
@Component({
  // On the host, not on the panel: Escape has to close the overlay wherever focus is.
  host: { '(document:keydown.escape)': 'close()' },
  imports: [DatePipe],
  selector: 'app-class-details-overlay',
  styleUrl: './class-details-overlay.scss',
  templateUrl: './class-details-overlay.html',
})
export class ClassDetailsOverlay {
  readonly row = input.required<ScheduledClass>();

  /** Whether the caller already holds an active booking on this class. */
  readonly booked = input(false);

  /** An action is in flight. Disables both buttons and swaps their labels. */
  readonly busy = input(false);

  /** A refusal, already turned into a Polish sentence by the screen. */
  readonly error = input<string | null>(null);

  readonly book = output<void>();

  /**
   * Named `cancelBooking` rather than `cancel` because `cancel` is a native DOM event: an output
   * that shadows one is how a host listener and a component output come to fight over the same name.
   */
  readonly cancelBooking = output<void>();
  readonly closed = output<void>();

  protected readonly endsAt = computed(
    () => new Date(new Date(this.row().startsAt).getTime() + this.row().durationMinutes * 60_000),
  );

  /**
   * Booking closes AT the start, matching the server's `class_started` rule exactly. Computed from
   * the row rather than tracked with a timer: an overlay open across the start of its own class is
   * not a case worth a subscription, and the server refuses it anyway.
   */
  protected readonly started = computed(
    () => new Date(this.row().startsAt).getTime() <= Date.now(),
  );

  protected readonly full = computed(() => this.row().freeSpots <= 0);

  /**
   * Booking is offered only when it could actually succeed. The alternative — always showing the
   * button and letting the server refuse — trades one clear sentence for a click that fails, and
   * the sentence is what the member needs either way.
   */
  protected readonly canBook = computed(() => !this.booked() && !this.started() && !this.full());

  /**
   * Cancelling has NO time rule. prd.md §Non-Goals locks free-cancel-anytime, so a booked member may
   * release the spot even after the class has started — the server applies no rule here either, and
   * a button that disappeared at the start would be this screen inventing one.
   */
  protected readonly canCancel = computed(() => this.booked());

  protected close(): void {
    this.closed.emit();
  }
}
