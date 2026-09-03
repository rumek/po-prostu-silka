import { Component, inject, signal } from '@angular/core';
import { ClassService } from '../../core/scheduling/class.service';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { CalendarRange, ScheduleCalendar } from '../../shared/calendar/schedule-calendar';

/**
 * The member's schedule (prd.md FR-007, prd-v2 FR-015, FR-016, FR-018).
 *
 * A CALENDAR since S-07, not the day-grouped list it was: one day at a time on a phone, the whole
 * week from 48rem up. The grouping this screen used to do in a `computed` moved into the shared
 * calendar, which is also what the admin panel renders — FR-017's whole point is that there is one
 * of them.
 *
 * What is left here is data: the calendar says which window it is showing, this fetches it. The
 * screen holds no navigation state at all, which is why moving between weeks needs no code here.
 */
@Component({
  imports: [ScheduleCalendar],
  selector: 'app-schedule',
  styleUrl: './schedule.scss',
  templateUrl: './schedule.html',
})
export class Schedule {
  private readonly classes = inject(ClassService);

  protected readonly rows = signal<ScheduledClass[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** The window the calendar is showing. Null until its first emission, which is the first load. */
  private readonly range = signal<CalendarRange | null>(null);

  /**
   * NEW IN S-07, and not optional. Nothing cancels an in-flight request, so the last RESPONSE would
   * otherwise win: two quick taps on "next week" can land their responses in either order, and the
   * loser would overwrite the week actually on screen. The single fetch this screen used to do could
   * not race with anything; navigation is what made it possible. Same guard as classes.ts.
   */
  private generation = 0;

  /** Driven by the calendar's rangeChange, which fires on init too — hence no ngOnInit. */
  protected async load(range: CalendarRange): Promise<void> {
    this.range.set(range);

    const generation = ++this.generation;

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const rows = await this.classes.getSchedule(range.from, range.to);
      if (generation !== this.generation) {
        return;
      }
      this.rows.set(rows);
    } catch {
      if (generation !== this.generation) {
        return;
      }
      this.loadFailed.set(true);
    } finally {
      if (generation === this.generation) {
        this.loading.set(false);
      }
    }
  }

  /**
   * Refetches the window on screen.
   *
   * The member's only recovery from a failed load. The calendar owns navigation, so there is no
   * gesture that would retry as a side effect — without this the answer to a dropped connection is
   * "reload the page", which is not an answer a schedule should give.
   */
  protected async reload(): Promise<void> {
    const range = this.range();

    if (range) {
      await this.load(range);
    }
  }
}
