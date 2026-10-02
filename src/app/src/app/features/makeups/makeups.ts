import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import { makeupFailureMessage } from '../../core/scheduling/makeup-failure';
import { MakeupItem } from '../../core/scheduling/makeup.models';
import { MakeupService } from '../../core/scheduling/makeup.service';
import { Checkbox } from '../../shared/forms/checkbox/checkbox';
import { createBusySet } from '../../shared/forms/busy-set';
import { Empty } from '../../shared/forms/empty/empty';
import { Field } from '../../shared/forms/field/field';
import { Loading } from '../../shared/forms/loading/loading';
import { createLoadFence } from '../../shared/forms/load-fence';
import { List } from '../../shared/list/list';
import { Row } from '../../shared/list/row';
import { ToastService } from '../../shared/toast/toast.service';
import { CLUB_ZONE } from '../../shared/class-date/class-date';
import { MakeupClassPicker } from './makeup-class-picker';
import { STATUS_WORDS, clubDay, instantDay, instantWhen } from './makeup-format';

/** The query parameter for "Pokaż zamknięte" — Polish, like the screen's other words. */
export const CLOSED_PARAM = 'zamkniete';

/** The list's state as the URL carries it. */
interface ListState {
  closed: boolean;
  page: number;
}

/** Reads the state out of the URL, and says whether the URL was already canonical. */
function readState(params: ParamMap): { state: ListState; canonical: boolean } {
  const rawClosed = params.get(CLOSED_PARAM);
  const rawPage = params.get('page');

  const closed = rawClosed === '1';
  const page = rawPage !== null && /^[1-9]\d{0,5}$/.test(rawPage) ? Number(rawPage) : 1;

  const canonical =
    (rawClosed === null || rawClosed === '1') &&
    (rawPage === null || (page > 1 && rawPage === String(page)));

  return { state: { closed, page }, canonical };
}

/** Today in the club's calendar, as YYYY-MM-DD — what a deadline is compared with. */
function clubToday(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: CLUB_ZONE }).format(new Date());
}

/**
 * "Odrabianie" (S-36) — the club's shared makeup sheet, as a screen. Every absence marked "odrobi" is
 * a row; staff book its one free makeup into any class within thirty days, release it, or close the
 * row as "nie odrobił".
 *
 * <h2>Admin and trainer, the same list</h2>
 *
 * Any trainer may arrange any member's makeup in any class, so both staff personas see the whole
 * club's list — one predicate in the menu, the guard (staffGuard) and the API (TrainerOrAdmin).
 *
 * <h2>Open by default, everything on request</h2>
 *
 * The working list is what still needs doing: "do odrobienia" and "zaplanowane", nearest deadline
 * first. "Pokaż zamknięte" (`zamkniete=1` in the URL, replacing the entry) adds the made-up and
 * not-made-up rows.
 *
 * <h2>Where a failure goes</h2>
 *
 * Row actions report by toast (S-19 outlet 3): the screen stays put and the row is refreshed from
 * the server's answer. A list that could not load is outlet 4. The picker keeps its own banner.
 */
@Component({
  imports: [Row, List, Empty, Loading, Field, Checkbox, MakeupClassPicker],
  selector: 'app-makeups',
  styleUrl: './makeups.scss',
  templateUrl: './makeups.html',
})
export class Makeups {
  private readonly makeups = inject(MakeupService);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly rows = signal<MakeupItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly loadMessage = signal<string | null>(null);

  protected readonly closed = signal(false);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly pageSize = signal(25);

  protected readonly paged = computed(() => this.total() > this.pageSize());
  protected readonly hasPrevious = computed(() => this.page() > 1);
  protected readonly hasNext = computed(() => this.page() * this.pageSize() < this.total());

  /** The item whose makeup is being chosen, or null while the picker is shut. */
  protected readonly picking = signal<MakeupItem | null>(null);

  protected readonly busy = createBusySet();
  private readonly fence = createLoadFence();

  protected readonly words = STATUS_WORDS;
  protected readonly clubDay = clubDay;
  protected readonly instantDay = instantDay;
  protected readonly when = instantWhen;

  constructor() {
    this.route.queryParamMap
      .pipe(takeUntilDestroyed())
      .subscribe((params) => void this.onParams(params));
  }

  /** A hand-closed item can be reopened while today is still within its deadline. */
  protected reopenable(item: MakeupItem): boolean {
    return item.closedByHand && clubToday() <= item.deadline;
  }

  /** A planned makeup can be released until its class starts. */
  protected releasable(item: MakeupItem): boolean {
    return (
      item.status === 'planned' &&
      item.makeup !== null &&
      new Date(item.makeup.startsAt).getTime() > Date.now()
    );
  }

  private async onParams(params: ParamMap): Promise<void> {
    const { state, canonical } = readState(params);

    if (!canonical) {
      await this.navigate(state, true);
      return;
    }

    this.closed.set(state.closed);
    this.page.set(state.page);
    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();

    this.loading.set(true);
    this.loadFailed.set(false);
    this.loadMessage.set(null);

    try {
      const page = this.page();
      const result = await this.makeups.list(this.closed(), page);

      if (!this.fence.isCurrent(generation)) {
        return;
      }

      // The page ran out from under the URL - rows closed since. Back to the last page that exists.
      if (result.items.length === 0 && page > 1) {
        const last = Math.max(1, Math.ceil(result.total / result.pageSize));
        if (last < page) {
          await this.navigate({ closed: this.closed(), page: last }, true);
          return;
        }
      }

      this.rows.set(result.items);
      this.total.set(result.total);
      this.pageSize.set(result.pageSize);
    } catch (failure) {
      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.loadMessage.set(transportMessage(classifyFailure(failure)));
      this.rows.set([]);
      this.loadFailed.set(true);
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.loading.set(false);
      }
    }
  }

  protected toggleClosed(): void {
    void this.navigate({ closed: !this.closed(), page: 1 }, true);
  }

  protected previousPage(): void {
    void this.navigate({ closed: this.closed(), page: this.page() - 1 }, false);
  }

  protected nextPage(): void {
    void this.navigate({ closed: this.closed(), page: this.page() + 1 }, false);
  }

  protected openPicker(item: MakeupItem): void {
    this.picking.set(item);
  }

  protected closePicker(): void {
    this.picking.set(null);
  }

  /** The picker booked a makeup: replace the row and say where. */
  protected onBooked(item: MakeupItem): void {
    this.picking.set(null);
    this.replace(item);
    this.toast.success(
      item.makeup
        ? `Zapisano odrabianie: ${item.makeup.name}, ${instantWhen(item.makeup.startsAt)}.`
        : 'Zapisano odrabianie.',
    );
  }

  protected release(item: MakeupItem): Promise<void> {
    return this.act(
      item,
      () => this.makeups.release(item.absenceBookingId),
      'Zwolniono odrabianie.',
    );
  }

  protected close(item: MakeupItem): Promise<void> {
    return this.act(
      item,
      () => this.makeups.close(item.absenceBookingId),
      'Oznaczono jako nieodrobione.',
    );
  }

  protected reopen(item: MakeupItem): Promise<void> {
    return this.act(item, () => this.makeups.reopen(item.absenceBookingId), 'Otwarto ponownie.');
  }

  /** One row action: busy on the row, the server's row replaces it, a toast either way. */
  private async act(
    item: MakeupItem,
    call: () => Promise<MakeupItem>,
    success: string,
  ): Promise<void> {
    if (this.busy.isBusy(item.absenceBookingId)) {
      return;
    }

    this.busy.setBusy(item.absenceBookingId, true);
    try {
      this.replace(await call());
      this.toast.success(success);
    } catch (error) {
      const info = classifyFailure(error);
      this.toast.error(transportMessage(info) ?? makeupFailureMessage(info.reason));
      void this.load();
    } finally {
      this.busy.setBusy(item.absenceBookingId, false);
    }
  }

  /**
   * Puts the server's row in place. On the open-only list a row that has just closed stays until the
   * next load, so the staff member sees what their tap did rather than a row vanishing under it.
   */
  private replace(updated: MakeupItem): void {
    this.rows.update((rows) =>
      rows.map((row) => (row.absenceBookingId === updated.absenceBookingId ? updated : row)),
    );
  }

  private navigate(state: ListState, replaceUrl: boolean): Promise<boolean> {
    return this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        [CLOSED_PARAM]: state.closed ? '1' : null,
        page: state.page > 1 ? state.page : null,
      },
      replaceUrl,
    });
  }
}
