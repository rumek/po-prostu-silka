import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';

/**
 * One class, said in three lines: when it runs, what it is, who leads it.
 *
 * PRIMITIVES, NOT A MODEL, and that is the whole reason this is a component rather than a shared
 * template fragment. The member's screens feed it from `MyBooking`; the admin dashboard feeds it from
 * `ScheduledClass`. Those are different records that happen to agree on these four fields, and typing
 * the input as either one would have locked the other out.
 *
 * READ-ONLY BY CONSTRUCTION. It renders no action, because its two callers disagree about what the
 * action is — `/my-classes` cancels a booking, the dashboard links to a screen — and a component that
 * takes an optional action ends up owning neither well. The caller wraps this in whatever row it
 * needs.
 *
 * It must not import date-fns. This renders inside the EAGER dashboard, and date-fns currently
 * reaches the bundle only through the lazy calendar chunk; pulling it in here would undo the split
 * that `app.routes.ts` went out of its way to make.
 */
@Component({
  imports: [DatePipe],
  selector: 'app-class-summary',
  styleUrl: './class-summary.scss',
  templateUrl: './class-summary.html',
})
export class ClassSummary {
  /** The class name, as the gym writes it. */
  readonly name = input.required<string>();

  /** ISO-8601 UTC, exactly as both APIs return it. */
  readonly startsAt = input.required<string>();

  readonly durationMinutes = input.required<number>();

  /** Display name of whoever leads it. */
  readonly instructor = input.required<string>();

  /**
   * Derived, never stored — the same rule the Class aggregate follows, and the reason neither API
   * returns an end time. Moved here from `MyClasses.endsAt` when this component was extracted.
   */
  protected readonly endsAt = computed(
    () => new Date(new Date(this.startsAt()).getTime() + this.durationMinutes() * 60_000),
  );
}
