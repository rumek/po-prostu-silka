import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { memberListFailureMessage } from '../../../core/admin/member-list-failure';
import { MemberListFailure } from '../../../core/admin/member-admin.models';
import { classifyFailure } from '../../../core/http/failure';
import { TrainingPlanService } from '../../../core/training/training-plan.service';
import { TrainerMember } from '../../../core/training/training-plan.models';
import { createLoadFence } from '../../../shared/forms/load-fence';
import { Loading } from '../../../shared/forms/loading/loading';
import { Empty } from '../../../shared/forms/empty/empty';
import { List } from '../../../shared/list/list';
import { Row } from '../../../shared/list/row';

/** The screen's page. Fixed, as on the admin's list. */
export const TRAINER_MEMBERS_PAGE_SIZE = 25;

/** How long typing has to pause before the phrase is searched. One request per pause, not per key. */
export const TRAINER_SEARCH_DEBOUNCE_MS = 300;

/** The API refuses a longer phrase (GetMembers.MaxSearchLength); the URL is clamped to it instead. */
const MAX_SEARCH_LENGTH = 100;

/** The list's state as the URL carries it. `page` is 1-based; `q` is already trimmed. */
interface ListState {
  q: string;
  page: number;
}

/**
 * Reads the list's state out of the query string, and says whether the URL was already in its
 * canonical form. A junk value (`page=abc`, `page=0`, a blank `q`) is dropped rather than sent to
 * the API, which would refuse it with a 400.
 */
function readState(params: ParamMap): { state: ListState; canonical: boolean } {
  const rawQ = params.get('q');
  const rawPage = params.get('page');

  const q = (rawQ ?? '').trim().slice(0, MAX_SEARCH_LENGTH);
  const page = rawPage !== null && /^[1-9]\d{0,5}$/.test(rawPage) ? Number(rawPage) : 1;

  const canonical =
    (rawQ === null || (q !== '' && rawQ === q)) &&
    (rawPage === null || (page > 1 && rawPage === String(page)));

  return { state: { q, page }, canonical };
}

/**
 * The trainer's member list (S-22, UX-08) — how a trainer reaches a member's plan. Active members
 * only, searched by NAME only, one page at a time; each row says whether the member has a plan and
 * opens it.
 *
 * <h2>A known duplicate of the admin list's URL state</h2>
 *
 * `q` and `page` live in the query string and ONLY the `queryParamMap` subscription loads, exactly as
 * in `members.ts`: the debounced search box replaces the history entry, the pager pushes, and a page
 * that ran out falls back to the last one that exists. This is a smaller second copy (no filter, no
 * row actions), recorded as such in the S-22 plan rather than extracted — extracting it would put the
 * freshly reviewed S-21 screen back under change for a structural gain nothing needs yet.
 *
 * <h2>What a trainer sees, and why it is so little</h2>
 *
 * Name, "bez konta", and the plan's name — the API sends nothing else. No e-mail, no status, no
 * phone: prd.md's privacy NFR keeps member data between the admin and the member. The search matches
 * names only, so it cannot answer "does anyone's address contain x" either.
 */
@Component({
  imports: [Row, List, Empty, Loading, FormsModule, RouterLink],
  selector: 'app-trainer-members',
  styleUrl: './trainer-members.scss',
  templateUrl: './trainer-members.html',
})
export class TrainerMembers {
  private readonly plans = inject(TrainingPlanService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly rows = signal<TrainerMember[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /**
   * The words for a refusal the API named (`invalid_page` / `invalid_search`), from the member-list
   * table the admin's list uses; null for any other failure, which keeps the screen's own sentence.
   */
  protected readonly loadMessage = signal<string | null>(null);

  /** The state the rows on screen were ASKED for — mirrored from the URL, never set directly. */
  protected readonly query = signal('');
  protected readonly page = signal(1);

  /** From the page envelope: how many match in all, and how many a page holds. */
  protected readonly total = signal(0);
  protected readonly pageSize = signal(TRAINER_MEMBERS_PAGE_SIZE);

  /** The page the rows on screen ARE — it lags `page` by one round-trip after a page change. */
  private readonly shownPage = signal(1);

  /** What is in the search box, which runs AHEAD of `query` by up to one debounce pause. */
  protected readonly searchInput = signal('');

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** One fence: the list is the screen's only independent load. */
  private readonly fence = createLoadFence();

  /** The 1-based range the pager reads out, e.g. `26–50 z 312`. */
  protected readonly rangeFrom = computed(() => (this.shownPage() - 1) * this.pageSize() + 1);
  protected readonly rangeTo = computed(() => this.rangeFrom() + this.rows().length - 1);

  /** The pager exists only when there is somewhere to go. */
  protected readonly paged = computed(() => this.total() > this.pageSize());
  protected readonly hasPrevious = computed(() => this.page() > 1);
  protected readonly hasNext = computed(() => this.page() * this.pageSize() < this.total());

  constructor() {
    this.route.queryParamMap
      .pipe(takeUntilDestroyed())
      .subscribe((params) => void this.onParams(params));

    inject(DestroyRef).onDestroy(() => this.cancelSearch());
  }

  /** The member's plan screen — the trainer's mount of the builder. */
  protected planLink(member: TrainerMember): unknown[] {
    return ['/trainer/members', member.id, 'plan'];
  }

  /**
   * Every URL change lands here. A URL that is not canonical is rewritten first — replacing the
   * entry, so Back does not return to the junk one — and the rewrite's own emission is what loads.
   */
  private async onParams(params: ParamMap): Promise<void> {
    const { state, canonical } = readState(params);

    if (!canonical) {
      await this.navigate(state, true);
      return;
    }

    this.query.set(state.q);
    this.page.set(state.page);

    // Only when the URL disagrees with the box: while the trainer types, the box is AHEAD of the URL.
    if (this.searchInput().trim() !== state.q) {
      this.cancelSearch();
      this.searchInput.set(state.q);
    }

    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();

    this.loading.set(true);
    this.loadFailed.set(false);
    this.loadMessage.set(null);

    try {
      const page = this.page();
      const result = await this.plans.getTrainerMembers({
        search: this.query() || undefined,
        page: page > 1 ? page : undefined,
        pageSize: TRAINER_MEMBERS_PAGE_SIZE,
      });

      if (!this.fence.isCurrent(generation)) {
        return;
      }

      // The page ran out from under the URL — a member blocked since, a bookmark from when the club
      // was bigger. Go to the last page that exists, replacing, and only ever backwards.
      if (result.items.length === 0 && page > 1) {
        const last = Math.max(1, Math.ceil(result.total / result.pageSize));

        if (last < page) {
          await this.navigate({ q: this.query(), page: last }, true);
          return;
        }
      }

      this.rows.set(result.items);
      this.shownPage.set(page);
      this.total.set(result.total);
      this.pageSize.set(result.pageSize);
    } catch (failure) {
      if (!this.fence.isCurrent(generation)) {
        return;
      }

      // Outlet 4: the list could not be populated. A refusal the API named takes the member-list
      // table's words; anything else keeps the screen's own sentence and its retry.
      const info = classifyFailure(failure);
      this.loadMessage.set(
        info.kind === 'business'
          ? memberListFailureMessage(info.reason as MemberListFailure['reason'] | undefined)
          : null,
      );

      this.rows.set([]);
      this.loadFailed.set(true);
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.loading.set(false);
      }
    }
  }

  /** The search box's every keystroke. Searches only once typing pauses. */
  protected onSearchInput(value: string): void {
    this.searchInput.set(value);
    this.cancelSearch();

    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      void this.commitSearch();
    }, TRAINER_SEARCH_DEBOUNCE_MS);
  }

  /** Replaces the history entry rather than pushing one, and goes back to page 1 — a new list. */
  private async commitSearch(): Promise<void> {
    const q = this.searchInput().trim().slice(0, MAX_SEARCH_LENGTH);
    if (q === this.query()) {
      return;
    }

    await this.navigate({ q, page: 1 }, true);
  }

  private cancelSearch(): void {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }

  protected async previousPage(): Promise<void> {
    if (this.hasPrevious()) {
      await this.goToPage(this.page() - 1);
    }
  }

  protected async nextPage(): Promise<void> {
    if (this.hasNext()) {
      await this.goToPage(this.page() + 1);
    }
  }

  /** Pushes, unlike search: a page the trainer chose is a place Back should return to. */
  private async goToPage(page: number): Promise<void> {
    // A phrase still waiting out its debounce is a NEW list; commit it (page 1) instead.
    const typed = this.searchInput().trim().slice(0, MAX_SEARCH_LENGTH);
    if (this.searchTimer !== null && typed !== this.query()) {
      this.cancelSearch();
      await this.commitSearch();
      return;
    }

    this.cancelSearch();
    await this.navigate({ q: this.query(), page }, false);
  }

  /** Writes the state to the URL. Defaults are left off, so the plain list is `/trainer/members`. */
  private async navigate(state: ListState, replaceUrl: boolean): Promise<void> {
    await this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        q: state.q || null,
        page: state.page > 1 ? state.page : null,
      },
      replaceUrl,
    });
  }
}
