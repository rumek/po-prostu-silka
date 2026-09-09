import { DatePipe } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { ScheduledClass } from '../../../core/scheduling/class.models';

/**
 * One class, as a member reads it (prd.md FR-007; S-16 MP-01).
 *
 * <h2>It has no actions any more</h2>
 *
 * The book and cancel buttons are gone: MP-01 removed self-service booking, so this overlay is
 * purely informational — the name, when it runs, what it is, who teaches it and how full it is. It
 * still exists because that information has nowhere else to live: a calendar tile has no room for a
 * description, and the description is the reason a member opens a class at all.
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
 * It renders what it is told and reports one intention — that the member closed it. That was already
 * true when it had buttons, and it is what makes it testable without HTTP.
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

  /**
   * Whether the caller already holds an active booking on this class.
   *
   * KEPT, though nothing acts on it: the overlay says so in a line of text, which is the only place
   * on the schedule a member learns they are already signed up for the class they are looking at.
   */
  readonly booked = input(false);

  readonly closed = output<void>();

  protected readonly endsAt = computed(
    () => new Date(new Date(this.row().startsAt).getTime() + this.row().durationMinutes * 60_000),
  );

  protected readonly full = computed(() => this.row().freeSpots <= 0);

  protected close(): void {
    this.closed.emit();
  }
}
