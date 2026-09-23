import { DatePipe } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { classFailureMessage } from '../../../core/scheduling/class-failure';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { Field } from '../../../shared/forms/field/field';
import { useOverlayFocus } from '../../../shared/forms/overlay-focus';

/** The duplicate range the server accepts; outside it the API answers `invalid_weeks`. */
const MIN_WEEKS = 1;
const MAX_WEEKS = 8;

/**
 * Which of the two destructive actions a class offers (S-09; prd.md FR-013).
 *
 * ONE BUTTON, NOT TWO. The two are mutually exclusive — a class somebody is signed up for cannot be
 * deleted, and cancelling one nobody booked would send zero messages and hide it from the schedule
 * for no reason.
 *
 * The `status` clause is a GUARD, not a case the admin screen reaches: the admin list no longer
 * returns cancelled classes at all. It stays because the check is one comparison and the
 * alternative — offering an action the server answers with `already_cancelled` — is the kind of
 * dead button that only appears once the list starts returning them again.
 */
export function canCancel(row: ScheduledClass): boolean {
  return row.status === 'Scheduled' && row.freeSpots < row.capacity;
}

/** How many people a cancellation will email and push. Derived — the wire carries free spots. */
export function bookedCount(row: ScheduledClass): number {
  return row.capacity - row.freeSpots;
}

/** Which face of the overlay is showing: the four actions, or one of the three confirmations. */
type Step = 'actions' | 'duplicate' | 'delete' | 'cancel';

/**
 * Everything an admin does to ONE class, opened by activating its tile (S-20, UX-03).
 *
 * <h2>Why the actions left the tile</h2>
 *
 * They used to be projected into the tile itself, which is `height: 100%; overflow: hidden` and one
 * pixel per minute tall — so for any class shorter than about ninety minutes, which is most of them,
 * Edytuj / Powiel / Zapisani / Odwołaj were clipped away with no scroll and no hint. The tile is now
 * a button (the calendar's `selectable`) and the actions live here, at the same size for a 30-minute
 * class as for a 3-hour one. The three confirmations that used to open as panels below the calendar
 * moved in too, so the admin never has to look away from the class they are acting on.
 *
 * Built exactly like `features/schedule/class-details-overlay` and this screen's other two overlays:
 * a backdrop button, a `.card.overlay-panel` dialog, Escape on the host, `useOverlayFocus()`.
 *
 * <h2>It renders and reports</h2>
 *
 * It performs no request. The screen owns the rows, the busy set, the toasts and every mutation —
 * and therefore also when this overlay closes: a successful action closes it, a failed one leaves it
 * open with {@link failure} set.
 */
@Component({
  // On the host, not on the panel: Escape has to close the overlay wherever focus is.
  host: { '(document:keydown.escape)': 'close()' },
  imports: [DatePipe, Field, FormsModule, RouterLink],
  selector: 'app-class-actions-overlay',
  styleUrl: './class-actions-overlay.scss',
  templateUrl: './class-actions-overlay.html',
})
export class ClassActionsOverlay {
  private readonly focus = useOverlayFocus(() => this.close());

  readonly row = input.required<ScheduledClass>();

  /** A mutation on this class is in flight — every action that would start another is disabled. */
  readonly busy = input(false);

  /**
   * Why the last action on this class failed, in the screen's words (S-19: never this template's).
   * The toast has already announced it, so this is not an alert — it keeps the reason beside the class.
   */
  readonly failure = input<string | null>(null);

  /**
   * A delete was refused with `has_bookings` — the dead end S-09 closes.
   *
   * The overlay offers Usuń because every ACTIVE booking has since been released; the server refuses
   * anyway, because it counts bookings that ever existed. Cancelling is the action the admin actually
   * wanted, so this turns the refusal into a way into the cancel step.
   */
  readonly deleteBlocked = input(false);

  readonly duplicateRequested = output<number>();
  readonly deleteRequested = output<void>();
  readonly cancelRequested = output<void>();
  readonly bookingsRequested = output<void>();
  readonly closed = output<void>();

  protected readonly step = signal<Step>('actions');

  /** How many following weeks a duplicate covers. Local: it means nothing until Powiel is confirmed. */
  protected readonly weeks = signal(4);

  /**
   * Powiel was pressed with a count the server would refuse (S-19 outlet 1: the refusal names this
   * field, so it is said under the field rather than in a toast after a round trip). The words are
   * the server's own `invalid_weeks` sentence, from the shared table.
   */
  protected readonly weeksRefused = signal(false);
  protected readonly weeksMessage = classFailureMessage('invalid_weeks');

  protected readonly endsAt = computed(
    () => new Date(new Date(this.row().startsAt).getTime() + this.row().durationMinutes * 60_000),
  );

  protected readonly cancellable = computed(() => canCancel(this.row()));
  protected readonly booked = computed(() => bookedCount(this.row()));

  protected setWeeks(value: number): void {
    this.weeks.set(value);
    this.weeksRefused.set(false);
  }

  protected confirmDuplicate(): void {
    const weeks = this.weeks();

    // The same range the server enforces — see `invalid_weeks` in class-failure.
    if (!Number.isInteger(weeks) || weeks < MIN_WEEKS || weeks > MAX_WEEKS) {
      this.weeksRefused.set(true);
      return;
    }

    this.duplicateRequested.emit(weeks);
  }

  protected show(step: Step): void {
    this.step.set(step);
  }

  protected close(): void {
    this.closed.emit();
  }
}
