import { Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { classFailureMessage } from '../../../core/scheduling/class-failure';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { ToastService } from '../../../shared/toast/toast.service';
import { ClassService } from '../../../core/scheduling/class.service';
import { DESK_MEDIA_QUERY } from '../../../core/layout/breakpoints';
import { mediaQuerySignal } from '../../../core/layout/media-query';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { adminCandidateSearch } from '../../../core/scheduling/booking-candidates';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import {
  CalendarRange,
  DrawnRange,
  RescheduledClass,
  ScheduleCalendar,
} from '../../../shared/calendar/schedule-calendar';
import { ClassActionsOverlay, bookedCount } from './class-actions-overlay';
import { ClassBookingsOverlay } from '../../class-bookings/class-bookings-overlay';
import { ClassCreateOverlay } from './class-create-overlay';
import { createBusySet } from '../../../shared/forms/busy-set';
import { createLoadFence } from '../../../shared/forms/load-fence';

/**
 * The admin's class management (prd-v2 FR-011, FR-012, FR-017).
 *
 * A CALENDAR since S-07, and deliberately the SAME one the member sees — that is the whole of
 * FR-017, whose stated failure mode is two calendars drifting apart. This screen adds its header
 * actions through content projection and its per-class actions through selection — activating a tile
 * opens `class-actions-overlay` (S-20) — rather than through a `mode` flag on the shared component,
 * so nothing admin-specific is compiled into the member's screen.
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
 * window has already ended, the tiles stop opening anything and a note says why. A tile that ignores
 * a click with no explanation reads as broken.
 *
 * <h2>Desk only (S-20)</h2>
 *
 * Below {@link DESK_MEDIA_QUERY} this screen renders a refusal instead of the calendar — a screen
 * state naming the reason and the surfaces that do work on a phone, not a grid that will not scroll
 * and gestures that will not land. Drawing, dragging and resizing a half-hour block inside a
 * scrolling 06:00–24:00 grid is a desk gesture. This NARROWS prd-v2 FR-019, which describes the
 * gestures without naming a device, and it reverses F5 of the calendar's impl review
 * (2026-09-02), which chose to make drawing work on the phone view instead — on the user's explicit
 * decision of 2026-09-20 (roadmap M-7 UX-01).
 */
@Component({
  imports: [
    ClassActionsOverlay,
    ClassBookingsOverlay,
    ClassCreateOverlay,
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

  /** Rows with a mutation in flight, so one slow row does not disable the whole list. */
  protected readonly busy = createBusySet();

  /**
   * The row whose last action failed, and the words for it — the same sentence the toast carried, so
   * the overlay can keep it beside the class. Cleared when another action starts.
   */
  protected readonly failed = signal<{ id: string; message: string } | null>(null);

  /**
   * Everything this screen says goes to the toast (S-19, outlet 3).
   *
   * Every message here answers a ROW action taken on a calendar the admin stays looking at — a
   * reschedule that rolled back, a duplicate that skipped a week, a cancellation that notified
   * people. It used to be a banner that ten different methods had to remember to clear.
   */
  private readonly toast = inject(ToastService);

  /**
   * The class whose actions overlay is open (S-20). Its confirmations — duplicate, delete, cancel —
   * are steps inside that overlay, so this one signal replaces the three panels below the calendar.
   */
  protected readonly selected = signal<ScheduledClass | null>(null);

  /**
   * {@link selected} as a list of at most one, so the template can key the overlay on the class id.
   * An `@if` would keep the same instance when the selection moves straight from one class to
   * another, carrying the old class's step, week count and focus over to the new one.
   */
  protected readonly selectedKeyed = computed(() => {
    const row = this.selected();

    return row ? [row] : [];
  });

  /**
   * Whether the delete refusal on screen is the one cancelling can resolve.
   *
   * The overlay cannot tell: it sees ACTIVE bookings only, while the server's `has_bookings` guard
   * refuses a delete once a class has EVER been booked, cancelled bookings included. So a class
   * everybody has since released offers "Usuń", gets refused, and would otherwise dead-end. This
   * flag turns that refusal into the one action that does work.
   */
  protected readonly deleteBlockedBy = signal<ScheduledClass | null>(null);

  /**
   * Which class has its sign-up list open (prd.md FR-014).
   *
   * Just the class: the list itself, its loading state and its per-row actions all live inside
   * `class-bookings-overlay`, the same way the create overlay owns its own two selects.
   */
  protected readonly viewingBookings = signal<ScheduledClass | null>(null);

  /** The bookings overlay's picker searches the admin member list here (S-25). */
  protected readonly candidateSearch = adminCandidateSearch(inject(MemberAdminService));

  /** The range drawn on the grid, awaiting a type and a trainer. Null when no overlay is open. */
  protected readonly drawn = signal<DrawnRange | null>(null);

  /** The whole visible window is behind us. Looking is fine; changing it is not. */
  protected readonly isPast = computed(() => {
    const range = this.range();

    return range !== null && range.to.getTime() <= Date.now();
  });

  /** The failure to show in the open actions overlay — only when it belongs to that class. */
  protected readonly selectedFailure = computed(() => {
    const failed = this.failed();

    return failed !== null && failed.id === this.selected()?.id ? failed.message : null;
  });

  /** See members.ts — nothing cancels an in-flight request, so the last RESPONSE would otherwise win. */
  private readonly fence = createLoadFence();

  /**
   * Whether the viewport is a desk's. Falls back to TRUE where nothing can be measured (a server,
   * jsdom): a screen that cannot tell should render, not refuse.
   */
  protected readonly desk = mediaQuerySignal(DESK_MEDIA_QUERY, true);

  constructor() {
    // Leaving the desk destroys the calendar, so everything that pointed into it goes too — the same
    // reasoning as a window change in `load`. Coming back re-creates the calendar, whose first
    // `rangeChange` reloads the current week.
    effect(() => {
      if (!this.desk()) {
        this.closeTransient();
      }
    });
  }

  protected async load(range: CalendarRange): Promise<void> {
    this.range.set(range);

    // A window change invalidates any open overlay: its class may not even be on screen any more.
    this.closeTransient();

    await this.fetchRows();
  }

  /**
   * Fetches the window in `range` into `rows`, and nothing else. Split out of `load` so a refetch of
   * the SAME window (after a duplicate, a create, a retry) leaves open overlays alone — only a window
   * change has a reason to close them.
   */
  private async fetchRows(): Promise<void> {
    const range = this.range();

    if (!range) {
      return;
    }

    const generation = this.fence.begin();

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const rows = await this.classes.getAdminClasses(range.from, range.to);
      if (!this.fence.isCurrent(generation)) {
        return;
      }
      this.rows.set(rows);
    } catch {
      if (!this.fence.isCurrent(generation)) {
        return;
      }
      this.loadFailed.set(true);
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.loading.set(false);
      }
    }
  }

  /** Closes every overlay that points at a class on the grid. */
  private closeTransient(): void {
    this.selected.set(null);
    this.deleteBlockedBy.set(null);
    this.viewingBookings.set(null);
    this.drawn.set(null);
  }

  /**
   * Closes the actions overlay only if it still shows `row`. A response can land after the admin
   * closed that overlay and opened another, and it must not take the newer one down with it.
   */
  private closeIfShowing(row: ScheduledClass): void {
    this.selected.update((open) => (open?.id === row.id ? null : open));
  }

  /** Refetches the window currently on screen — after a duplicate, a create, or a retry. */
  protected async reload(): Promise<void> {
    await this.fetchRows();
  }

  /** A gesture on empty grid (prd-v2 FR-019). The calendar withholds it entirely in a past week. */
  protected openCreate(range: DrawnRange): void {
    this.failed.set(null);
    this.closeTransient();
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
    const generation = this.fence.current();

    this.failed.set(null);
    this.rows.update((rows) =>
      rows.map((candidate) =>
        candidate.id === row.id
          ? { ...candidate, startsAt, durationMinutes: change.durationMinutes }
          : candidate,
      ),
    );
    this.busy.setBusy(row.id, true);

    try {
      await this.classes.update(row.id, {
        classTypeId: row.classTypeId,
        startsAt,
        durationMinutes: change.durationMinutes,
        instructorMemberId: row.instructorMemberId,
        capacity: row.capacity,
      });
    } catch (failure) {
      if (!this.fence.isCurrent(generation)) {
        // A different week is on screen. Neither the rows nor the message belong to it any more.
        return;
      }

      // Back to exactly what was on screen before the gesture — the block returns to its old slot,
      // which is the only honest picture once the server has refused. UNCHANGED by S-19: only where
      // the words come from moved.
      this.rows.set(before);

      this.toast.error(messageFor(failure));
    } finally {
      this.busy.setBusy(row.id, false);
    }
  }

  /**
   * A tile was activated (S-20). The calendar reports only a real click or a keyboard activation —
   * the click that ends a drag or a resize never arrives here — and never in a past week, where the
   * tiles are not selectable at all.
   */
  protected select(row: ScheduledClass): void {
    this.failed.set(null);
    // Every opener closes every other overlay first. Nothing traps Tab inside an overlay, so a tile
    // behind the create overlay can still be reached and activated — and two modals are one too many.
    this.closeTransient();
    this.selected.set(row);
  }

  protected closeSelected(): void {
    this.selected.set(null);
    this.deleteBlockedBy.set(null);
  }

  /**
   * Opens the sign-up list for a class (prd.md FR-014). SWAPS the actions overlay for the bookings
   * one rather than stacking them: two modals over one calendar is one too many to Escape out of.
   */
  protected openBookings(row: ScheduledClass): void {
    this.failed.set(null);
    this.closeTransient();
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

  /**
   * The overlay signed somebody up (S-14). Unlike `afterRelease` this REPLACES the class rather than
   * adjusting a number: the admin route answers with the occurrence as it now stands, and a count
   * the server just computed beats one this screen infers.
   */
  protected afterAdminBooking(updated: ScheduledClass): void {
    this.rows.update((rows) =>
      rows.map((candidate) => (candidate.id === updated.id ? updated : candidate)),
    );

    this.viewingBookings.update((open) => (open && open.id === updated.id ? updated : open));
  }

  protected async duplicate(row: ScheduledClass, weeks: number): Promise<void> {
    this.failed.set(null);
    this.busy.setBusy(row.id, true);

    try {
      const result = await this.classes.duplicate(row.id, weeks);
      this.closeIfShowing(row);

      // The whole point of the endpoint's contract: say what actually happened, per week. Doubly so
      // now that the copies land in weeks this view is not showing — the message is the only place
      // the admin learns they exist.
      // `info` rather than `success` when weeks were skipped: something did NOT happen that the
      // admin asked for, and a green tick over that would be the wrong answer.
      const outcome =
        result.skippedWeeks.length === 0
          ? `Utworzono ${result.created} ${this.copiesWord(result.created)} w kolejnych tygodniach.`
          : `Utworzono ${result.created} ${this.copiesWord(result.created)}. ` +
            `Pominięto tydzień ${result.skippedWeeks.join(', ')} — o tej porze są już inne zajęcia.`;

      if (result.skippedWeeks.length === 0) {
        this.toast.success(outcome);
      } else {
        this.toast.info(outcome);
      }

      await this.reload();
    } catch (failure) {
      // `invalid_weeks` needs no branch of its own any more: the overlay refuses an out-of-range count
      // under its field before asking (S-19 outlet 1), so it lands here only if the server's range
      // and the overlay's ever disagree — and then it reads through the same table as any refusal.
      const message = messageFor(failure);

      this.toast.error(message);
      this.failed.set({ id: row.id, message });
    } finally {
      this.busy.setBusy(row.id, false);
    }
  }

  protected async remove(row: ScheduledClass): Promise<void> {
    this.failed.set(null);
    this.busy.setBusy(row.id, true);

    try {
      await this.classes.remove(row.id);
      this.closeIfShowing(row);

      // A deleted class genuinely leaves the window — removing it locally is the honest
      // representation, and avoids a refetch that would only confirm what we already know.
      this.rows.update((rows) => rows.filter((r) => r.id !== row.id));
    } catch (failure) {
      // NAMED, not a generic "nie udało się". Since S-08 the likely refusal is has_bookings, and
      // "someone signed up" is the difference between a broken button and a rule the admin can act
      // on — by opening Zapisani, which is one step back in the overlay.
      const info = classifyFailure(failure);

      const message = messageFor(failure);

      this.toast.error(message);
      this.failed.set({ id: row.id, message });

      // The dead end S-09 closes. The overlay offered "Usuń" because every booking on this class has
      // since been released; the server refuses anyway, because it counts bookings that ever
      // existed. Cancelling is the action the admin actually wanted, and the overlay now offers it
      // one click away instead of leaving it unreachable.
      if (info.reason === 'has_bookings' && row.status === 'Scheduled') {
        this.deleteBlockedBy.set(row);
      }
    } finally {
      this.busy.setBusy(row.id, false);
    }
  }

  /**
   * The transition itself.
   *
   * THE TILE GOES, THE RECORD STAYS. A cancelled class leaves the admin's calendar the same way it
   * leaves the member's — the messages have gone out, the hour is free again for the overlap rule,
   * and a tile that still sits there is a slot that looks taken and is not. The class and its
   * bookings survive in the database, so who was signed up remains on record; it is the CALENDAR
   * the cancellation leaves, not the history. Dropping the row locally is therefore the honest
   * picture, and it matches what a refetch of this window would now return.
   *
   * Fenced like every other write here — a week change while this is in flight must not edit the
   * rows of a window this response does not belong to.
   */
  protected async cancel(row: ScheduledClass): Promise<void> {
    this.failed.set(null);
    this.deleteBlockedBy.set(null);
    this.busy.setBusy(row.id, true);

    const generation = this.fence.current();
    const told = bookedCount(row);

    try {
      await this.classes.cancel(row.id);

      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.closeIfShowing(row);
      this.rows.update((rows) => rows.filter((r) => r.id !== row.id));

      // Says what actually happened, like the duplicate outcome does. The messages are the point of
      // the action, and nothing else on this screen will ever show that they went out.
      this.toast.success(
        told === 0
          ? `Odwołano „${row.name}”.`
          : `Odwołano „${row.name}”. Powiadomiliśmy ${told} ${this.peopleWord(told)}.`,
      );
    } catch (failure) {
      if (!this.fence.isCurrent(generation)) {
        return;
      }

      // class_started and already_cancelled both land here, and both read through the shared table.
      const message = messageFor(failure);

      this.toast.error(message);
      this.failed.set({ id: row.id, message });
    } finally {
      this.busy.setBusy(row.id, false);
    }
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

  /** Polish plural for "osoba" in the accusative the notice needs — 1 osobę, 2–4 osoby, else osób. */
  private peopleWord(count: number): string {
    if (count === 1) {
      return 'osobę';
    }

    const lastTwo = count % 100;
    const last = count % 10;
    const isFew = last >= 2 && last <= 4 && !(lastTwo >= 12 && lastTwo <= 14);

    return isFew ? 'osoby' : 'osób';
  }
}

/**
 * The words for a refused class write.
 *
 * `transportMessage` first: a 429, a 500 or a dead network is not a scheduling rule, and telling the
 * admin to "wybierz inny termin" over one would send them to change a time that was never wrong.
 */
function messageFor(failure: unknown): string {
  const info = classifyFailure(failure);

  return transportMessage(info) ?? classFailureMessage(info.reason);
}
