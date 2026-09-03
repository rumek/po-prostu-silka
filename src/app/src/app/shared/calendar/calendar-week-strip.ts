import { DatePipe } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { addDays, startOfDay, startOfWeek } from 'date-fns';

/**
 * The strip's labels, Monday first.
 *
 * Written out rather than formatted from the locale on purpose. CLDR's Polish abbreviations are
 * "pon.", "wt.", "śr." — four characters with a full stop, which do not fit nine 32px cells across a
 * phone — and the narrow forms collide, giving Monday and Friday the same letter. These are the
 * two-letter forms Polish actually uses on a calendar. The library's own rendering still goes through
 * LOCALE_ID; this is chrome, not data.
 */
const WEEKDAY_LABELS = ['Pn', 'Wt', 'Śr', 'Cz', 'Pt', 'So', 'Nd'];

/**
 * The day view's whole navigation: the week you are in, with the day you are reading marked
 * (prd-v2 FR-015).
 *
 * A day at a time is the right amount of schedule on a phone, but a poor way to MOVE around one —
 * reaching Friday meant four presses of an arrow, or a trip to the date picker. The strip makes the
 * week the unit you navigate and the day the unit you read, and having done that it replaces the day
 * arrows and the "today" button rather than sitting beside them: two navigations for one view is how
 * a toolbar stops being read at all.
 *
 * Its own component rather than more markup in {@link ScheduleCalendar}, which was already carrying a
 * toolbar, a grid, a drag gesture and two overlays. It owns no state: it is told which day is current
 * and says which day was asked for.
 */
@Component({
  imports: [DatePipe],
  selector: 'app-calendar-week-strip',
  styleUrl: './calendar-week-strip.scss',
  templateUrl: './calendar-week-strip.html',
})
export class CalendarWeekStrip {
  /** The day being read. Always a local midnight — the calendar's anchor. */
  readonly anchor = input.required<Date>();

  /** A different day was asked for, by an arrow or by a button. The parent moves; this does not. */
  readonly anchorChange = output<Date>();

  protected readonly days = computed(() => {
    const start = startOfWeek(this.anchor(), { weekStartsOn: 1 });
    const selected = this.anchor().getTime();
    const today = startOfDay(new Date()).getTime();

    return WEEKDAY_LABELS.map((label, index) => {
      const date = addDays(start, index);

      return {
        date,
        label,
        isSelected: date.getTime() === selected,
        isToday: date.getTime() === today,
      };
    });
  });

  /**
   * A week in either direction, keeping the weekday.
   *
   * Stepping from the anchor rather than from the week start is what keeps Wednesday on Wednesday —
   * stepping the week and re-selecting its Monday would silently move the reader two days as well.
   */
  protected stepWeeks(weeks: number): void {
    this.anchorChange.emit(addDays(this.anchor(), weeks * 7));
  }

  protected select(date: Date): void {
    this.anchorChange.emit(date);
  }
}
