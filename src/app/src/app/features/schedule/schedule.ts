import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ClassService } from '../../core/scheduling/class.service';
import { ScheduledClass } from '../../core/scheduling/class.models';

/** One day heading and the classes under it. */
interface ScheduleDay {
  /** Midnight local on that day — what the heading formats. */
  day: Date;
  classes: ScheduledClass[];
}

/**
 * The member's schedule (FR-007): the next fortnight as a day-by-day list.
 *
 * A LIST, not a calendar grid — the PRD calls that out explicitly, because a weekly grid is painful
 * on a phone and this app is mobile-first.
 *
 * The API returns a flat, time-ordered list and this screen groups it. Grouping happens HERE, by the
 * browser's local date, for the same reason every other timestamp in this app renders in the
 * browser's clock: a member reads the schedule on their own device. The server never picks a
 * timezone for the read path.
 */
@Component({
  imports: [DatePipe],
  selector: 'app-schedule',
  styleUrl: './schedule.scss',
  templateUrl: './schedule.html',
})
export class Schedule implements OnInit {
  private readonly classes = inject(ClassService);

  protected readonly rows = signal<ScheduledClass[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /**
   * The flat list folded into day sections, in local time.
   *
   * Keyed on the LOCAL calendar date, so a class at 23:30 belongs to that evening rather than being
   * pushed into the next day by a UTC-based key.
   */
  protected readonly days = computed<ScheduleDay[]>(() => {
    const groups = new Map<string, ScheduleDay>();

    for (const row of this.rows()) {
      const startsAt = new Date(row.startsAt);
      const day = new Date(startsAt.getFullYear(), startsAt.getMonth(), startsAt.getDate());
      const key = day.toDateString();

      const existing = groups.get(key);
      if (existing) {
        existing.classes.push(row);
      } else {
        groups.set(key, { day, classes: [row] });
      }
    }

    // The API already ordered by time, and Map preserves insertion order, so the days and the
    // classes within each day are both already in order.
    return [...groups.values()];
  });

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      this.rows.set(await this.classes.getSchedule());
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  /** When the class ends, for display. Derived rather than stored — see the Class aggregate. */
  protected endsAt(row: ScheduledClass): Date {
    return new Date(new Date(row.startsAt).getTime() + row.durationMinutes * 60_000);
  }
}
