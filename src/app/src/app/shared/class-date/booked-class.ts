import { Component, computed, input } from '@angular/core';
import { CLOCK, ClassDate } from './class-date';

/**
 * One of the member's classes, as "Moje zajęcia" draws it: the stacked date column, the class name,
 * and "time · instructor" underneath. Both tabs there and the dashboard's "Najbliższe zajęcia" card
 * render it, so the three cannot drift.
 *
 * <p>It fills a container rather than being one. On the list screens it sits inside
 * <c>li[appRow]</c>, which owns the card and the list semantics; on the dashboard it sits straight in
 * the card. Anything that belongs to the far end of the row — the history's status dot — is
 * projected, and sets its own alignment — styles here cannot reach it, since projected content
 * carries the caller's encapsulation scope.</p>
 *
 * <p>PRIMITIVES, NOT A MODEL, for the reason <c>class-summary</c> gives: a booking and a history entry
 * are different records that agree on these fields.</p>
 */
@Component({
  imports: [ClassDate],
  selector: 'app-booked-class',
  styleUrl: './booked-class.scss',
  template: `
    <app-class-date [startsAt]="startsAt()" />

    <div class="booked-class-text">
      <p class="row-name" [class.booked-class-struck]="struck()">{{ name() }}</p>
      <p class="row-meta">{{ time() }} · {{ instructor() }}</p>
    </div>

    <ng-content />
  `,
})
export class BookedClass {
  readonly name = input.required<string>();

  /** ISO-8601 UTC, as the API returns it. */
  readonly startsAt = input.required<string>();

  readonly instructor = input.required<string>();

  /**
   * With a duration the time reads as a range, "18:00–19:00" — what an upcoming class needs. The
   * history passes none: a class that has happened is placed by when it started.
   */
  readonly durationMinutes = input<number | null>(null);

  /** A cancelled class, crossed out; the caller says why next to it. */
  readonly struck = input(false);

  protected readonly time = computed(() => {
    const start = new Date(this.startsAt());
    const minutes = this.durationMinutes();

    if (minutes === null) {
      return CLOCK.format(start);
    }

    // Derived, never stored — the API returns no end time.
    return `${CLOCK.format(start)}–${CLOCK.format(new Date(start.getTime() + minutes * 60_000))}`;
  });
}
