import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { classFailureMessage } from '../../../core/scheduling/class-failure';
import { ClassService } from '../../../core/scheduling/class.service';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import {
  CalendarRange,
  DrawnRange,
  RescheduledClass,
  ScheduleCalendar,
} from '../../../shared/calendar/schedule-calendar';
import { ClassBookingsOverlay } from './class-bookings-overlay';
import { ClassCreateOverlay } from './class-create-overlay';

/**
 * The admin's class management (prd-v2 FR-011, FR-012, FR-017).
 *
 * A CALENDAR since S-07, and deliberately the SAME one the member sees — that is the whole of
 * FR-017, whose stated failure mode is two calendars drifting apart. This screen adds its actions
 * through content projection rather than through a `mode` flag on the shared component, so nothing
 * admin-specific is compiled into the member's screen.
 *
 * Everything this screen did as a list, it still does: per-row busy tracking so one slow row does not
 * disable the rest, a generation guard so a late refetch cannot overwrite fresher rows, partial
 * success reported per week on duplicate, and an inline delete confirmation rather than confirm(),
 * which blocks the event loop and has no precedent in this codebase.
 *
 * <h2>Past weeks are read-only</h2>
 *
 * Navigating backwards is possible for the first time (the list used to start at now). Looking is the
 * point; editing history is not, and the API refuses a create in the past anyway — so when the visible
 * window has already ended, the actions are withheld and a note says why. A missing button with no
 * explanation reads as broken.
 */
@Component({
  imports: [
    ClassBookingsOverlay,
    ClassCreateOverlay,
    DatePipe,
    FormsModule,
    RouterLink,
    ScheduleCalendar,
  ],
  selector: 'app-classes',
  styleUrl: './classes.scss',
  templateUrl: './classes.html',
})
export class Classes {
  private readonly classes = inject(ClassService);

  protected readonly rows = signal<ScheduledClass[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** The window the calendar is showing. Null until its first emission, which is the first load. */
  protected readonly range = signal<CalendarRange | null>(null);

  /** Ids with an action in flight. */
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  /** Id of the row whose action failed. Cleared when another action starts. */
  protected readonly failedId = signal<string | null>(null);

  /** A screen-level message — the duplicate outcome, or a refusal retrying cannot fix. */
  protected readonly notice = signal<string | null>(null);

  /** Which class has its duplicate control open, and for how many weeks. */
  protected readonly duplicating = signal<ScheduledClass | null>(null);
  protected readonly weeks = signal(4);

  /** Which class is asking to confirm a delete. */
  protected readonly confirmingDelete = signal<ScheduledClass | null>(null);

  /**
   * Which class has its sign-up list open (prd.md FR-014).
   *
   * Just the class: the list itself, its loading state and its per-row actions all live inside
   * `class-bookings-overlay`, the same way the create overlay owns its own two selects.
   */
  protected readonly viewingBookings = signal<ScheduledClass | null>(null);

  /** The range drawn on the grid, awaiting a type and a trainer. Null when no overlay is open. */
  protected readonly drawn = signal<DrawnRange | null>(null);

  /** The whole visible window is behind us. Looking is fine; changing it is not. */
  protected readonly isPast = computed(() => {
    const range = this.range();

    return range !== null && range.to.getTime() <= Date.now();
  });

  /** See members.ts — nothing cancels an in-flight request, so the last RESPONSE would otherwise win. */
  private generation = 0;

  protected async load(range: CalendarRange): Promise<void> {
    this.range.set(range);

    // A window change invalidates any open panel: its class may not even be on screen any more.
    this.duplicating.set(null);
    this.confirmingDelete.set(null);
    this.viewingBookings.set(null);
    this.drawn.set(null);

    const generation = ++this.generation;

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const rows = await this.classes.getAdminClasses(range.from, range.to);
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

  /** Refetches the window currently on screen — after a duplicate, or a retry. */
  protected async reload(): Promise<void> {
    const range = this.range();

    if (range) {
      await this.load(range);
    }
  }

  /** A gesture on empty grid (prd-v2 FR-019). The calendar withholds it entirely in a past week. */
  protected openCreate(range: DrawnRange): void {
    this.notice.set(null);
    this.failedId.set(null);
    this.duplicating.set(null);
    this.confirmingDelete.set(null);
    this.drawn.set(range);
  }

  protected closeCreate(): void {
    this.drawn.set(null);
  }

  protected async afterCreate(): Promise<void> {
    this.drawn.set(null);
    // The new class is inside the visible window by construction — it was drawn there.
    await this.reload();
  }

  /**
   * A class was dragged to a new time or resized (prd-v2 FR-019).
   *
   * OPTIMISTIC, unlike every other write on this screen. The admin has just watched the block land
   * where they put it; snapping it back to the old time for the length of a round trip and then
   * moving it again is the one thing that would make a direct-manipulation gesture feel broken. The
   * previous rows are kept so a refusal can put it back exactly, in one step.
   *
   * The fields the gesture cannot express — type, trainer, capacity — are sent back unchanged. The
   * update endpoint takes a whole ClassRequest, so omitting them would blank them.
   */
  protected async reschedule(change: RescheduledClass): Promise<void> {
    const row = change.class;
    const startsAt = change.startsAt.toISOString();
    const before = this.rows();
    // The rollback below replaces the WHOLE row array, so it has to be fenced like every other write
    // here: navigating to another week while the PUT is in flight would otherwise restore the
    // previous window's rows over the one now on screen.
    const generation = this.generation;

    this.notice.set(null);
    this.failedId.set(null);
    this.rows.update((rows) =>
      rows.map((candidate) =>
        candidate.id === row.id
          ? { ...candidate, startsAt, durationMinutes: change.durationMinutes }
          : candidate,
      ),
    );
    this.setBusy(row.id, true);

    try {
      await this.classes.update(row.id, {
        classTypeId: row.classTypeId,
        startsAt,
        durationMinutes: change.durationMinutes,
        instructorUserId: row.instructorUserId,
        capacity: row.capacity,
      });
    } catch (failure) {
      if (generation !== this.generation) {
        // A different week is on screen. Neither the rows nor the message belong to it any more.
        return;
      }

      // Back to exactly what was on screen before the gesture — the block returns to its old slot,
      // which is the only honest picture once the server has refused.
      this.rows.set(before);

      const reason = ((failure as HttpErrorResponse)?.error as { reason?: string } | undefined)
        ?.reason;

      this.notice.set(classFailureMessage(reason));
    } finally {
      this.setBusy(row.id, false);
    }
  }

  protected openDuplicate(row: ScheduledClass): void {
    this.notice.set(null);
    this.failedId.set(null);
    this.confirmingDelete.set(null);
    this.viewingBookings.set(null);
    this.duplicating.set(this.duplicating()?.id === row.id ? null : row);
  }

  /** Opens the sign-up list for a class (prd.md FR-014). */
  protected openBookings(row: ScheduledClass): void {
    this.notice.set(null);
    this.failedId.set(null);
    this.duplicating.set(null);
    this.confirmingDelete.set(null);
    this.viewingBookings.set(row);
  }

  protected closeBookings(): void {
    this.viewingBookings.set(null);
  }

  /**
   * The overlay released a spot. Patches the class's freeSpots so the tile behind the overlay stops
   * disagreeing with the list in front of it.
   *
   * NOT fenced, unlike the writes above, and deliberately: a generation fence compares a value
   * captured BEFORE an await against the current one, and this method has no await to straddle. It
   * is called synchronously by the overlay once its own request has resolved, so there is no window
   * in which the generation could move underneath it. What actually guards the stale-window case is
   * that `load` closes the overlay, so a released spot from a previous week has no one to report it.
   */
  protected afterRelease(row: ScheduledClass): void {
    this.rows.update((rows) =>
      rows.map((candidate) =>
        candidate.id === row.id ? { ...candidate, freeSpots: candidate.freeSpots + 1 } : candidate,
      ),
    );

    // The overlay holds a snapshot of the class it was opened with, so the count in its own header
    // has to move too.
    this.viewingBookings.update((open) =>
      open && open.id === row.id ? { ...open, freeSpots: open.freeSpots + 1 } : open,
    );
  }

  protected closeDuplicate(): void {
    this.duplicating.set(null);
  }

  protected async duplicate(row: ScheduledClass): Promise<void> {
    this.failedId.set(null);
    this.notice.set(null);
    this.setBusy(row.id, true);

    try {
      const result = await this.classes.duplicate(row.id, this.weeks());
      this.duplicating.set(null);

      // The whole point of the endpoint's contract: say what actually happened, per week. Doubly so
      // now that the copies land in weeks this view is not showing — the message is the only place
      // the admin learns they exist.
      this.notice.set(
        result.skippedWeeks.length === 0
          ? `Utworzono ${result.created} ${this.copiesWord(result.created)} w kolejnych tygodniach.`
          : `Utworzono ${result.created} ${this.copiesWord(result.created)}. ` +
              `Pominięto tydzień ${result.skippedWeeks.join(', ')} — o tej porze są już inne zajęcia.`,
      );

      await this.reload();
    } catch (failure) {
      const reason = ((failure as HttpErrorResponse)?.error as { reason?: string } | undefined)
        ?.reason;

      if (reason === 'invalid_weeks') {
        // Through the shared table, so this reads the same here as it would anywhere else.
        this.notice.set(classFailureMessage(reason));
        return;
      }

      this.failedId.set(row.id);
    } finally {
      this.setBusy(row.id, false);
    }
  }

  protected confirmDelete(row: ScheduledClass): void {
    this.notice.set(null);
    this.failedId.set(null);
    this.duplicating.set(null);
    this.viewingBookings.set(null);
    this.confirmingDelete.set(row);
  }

  protected cancelDelete(): void {
    this.confirmingDelete.set(null);
  }

  protected async remove(row: ScheduledClass): Promise<void> {
    this.failedId.set(null);
    this.notice.set(null);
    this.setBusy(row.id, true);

    try {
      await this.classes.remove(row.id);
      this.confirmingDelete.set(null);

      // A deleted class genuinely leaves the window — removing it locally is the honest
      // representation, and avoids a refetch that would only confirm what we already know.
      this.rows.update((rows) => rows.filter((r) => r.id !== row.id));
    } catch (failure) {
      // NAMED, not a generic "nie udało się". Since S-08 the likely refusal is has_bookings, and
      // "someone signed up" is the difference between a broken button and a rule the admin can act
      // on — by opening Zapisani, which is right there.
      const reason = ((failure as HttpErrorResponse)?.error as { reason?: string } | undefined)
        ?.reason;

      this.notice.set(classFailureMessage(reason));
      this.failedId.set(row.id);
    } finally {
      this.setBusy(row.id, false);
    }
  }

  protected isBusy(id: string): boolean {
    return this.busy().has(id);
  }

  /** Polish plural for "kopia" — 1 kopię, 2–4 kopie, else kopii. */
  private copiesWord(count: number): string {
    if (count === 1) {
      return 'kopię';
    }

    const lastTwo = count % 100;
    const last = count % 10;
    const isFew = last >= 2 && last <= 4 && !(lastTwo >= 12 && lastTwo <= 14);

    return isFew ? 'kopie' : 'kopii';
  }

  private setBusy(id: string, value: boolean): void {
    this.busy.update((ids) => {
      const next = new Set(ids);
      if (value) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }
}
