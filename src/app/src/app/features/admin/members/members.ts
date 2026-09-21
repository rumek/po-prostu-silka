import { Component, DestroyRef, HostListener, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DOCUMENT, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import { accessCodeFailureMessage } from '../../../core/admin/access-code-failure';
import { blockFailureMessage } from '../../../core/admin/block-failure';
import { trainerRoleFailureMessage } from '../../../core/admin/trainer-role-failure';
import { unblockFailureMessage } from '../../../core/admin/unblock-failure';
import { ROLES } from '../../../core/auth/roles';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { ToastService } from '../../../shared/toast/toast.service';
import { AccessCodeView, Member, MemberFilter } from '../../../core/admin/member-admin.models';
import { createBusySet } from '../../../shared/forms/busy-set';
import { createLoadFence } from '../../../shared/forms/load-fence';
import { Loading } from '../../../shared/forms/loading/loading';
import { Empty } from '../../../shared/forms/empty/empty';

/** The filter positions, including "everyone". `null` means no filter parameter is sent. */
type StatusFilter = MemberFilter | null;

/** Stable DOM id for a row's menu trigger, so Escape can return focus to it. */
const triggerId = (memberId: string): string => `member-menu-${memberId}`;

/** The screen's page. Fixed — there is no page-size chooser (S-21 scope). */
export const MEMBERS_PAGE_SIZE = 25;

/** How long typing has to pause before the phrase is searched. One request per pause, not per key. */
export const SEARCH_DEBOUNCE_MS = 300;

/** The API refuses a longer phrase (GetMembers.MaxSearchLength); the URL is clamped to it instead. */
const MAX_SEARCH_LENGTH = 100;

const FILTERS: readonly MemberFilter[] = ['Active', 'Blocked', 'WithoutAccount'];

/** The list's state as the URL carries it. `page` is 1-based; `q` is already trimmed. */
interface ListState {
  q: string;
  filter: StatusFilter;
  page: number;
}

/**
 * Reads the list's state out of the query string, and says whether the URL was already in its
 * canonical form. A junk value (`page=abc`, `filter=Foo`, `page=0`, a blank `q`) is dropped rather
 * than sent to the API, which would refuse it with a 400.
 */
function readState(params: ParamMap): { state: ListState; canonical: boolean } {
  const rawQ = params.get('q');
  const rawFilter = params.get('filter');
  const rawPage = params.get('page');

  const q = (rawQ ?? '').trim().slice(0, MAX_SEARCH_LENGTH);
  const filter = FILTERS.find((f) => f === rawFilter) ?? null;
  const page = rawPage !== null && /^[1-9]\d{0,5}$/.test(rawPage) ? Number(rawPage) : 1;

  const canonical =
    (rawQ === null || (q !== '' && rawQ === q)) &&
    (rawFilter === null || rawFilter === filter) &&
    (rawPage === null || (page > 1 && rawPage === String(page)));

  return { state: { q, filter, page }, canonical };
}

/**
 * The admin's member list (FR-004, FR-005): everyone, filterable by status, searchable by name or
 * email, one page at a time, with block and unblock per row.
 *
 * <h2>The server pages and searches (S-21)</h2>
 *
 * S-02 split the work the other way — the filter refetched, search narrowed the loaded rows — on the
 * grounds that a club's list fits on one screen. At hundreds of members it does not, so the API now
 * answers the filter, the phrase and the page together, and typing costs one request per
 * {@link SEARCH_DEBOUNCE_MS} pause.
 *
 * <h2>The URL is the list's state</h2>
 *
 * `q`, `filter` and `page` live in the query string, and ONLY the `queryParamMap` subscription
 * loads a new view: chips, the pager and the debounced search box navigate, they never call
 * `load()`. That is what makes "Edytuj dane" and Back, or a reload, land on the same page with the
 * same phrase — and it leaves the out-of-order case to the load fence rather than inventing a second
 * guard. Search navigations replace the history entry so typing does not fill the back stack; chips
 * and the pager push.
 *
 * The approvals screen (S-01) stays as it is. Approve appears here too, on pending rows, so that a
 * pending member found through this screen is actionable where they are found rather than sending
 * the admin somewhere else to do the obvious thing.
 */
@Component({
  imports: [Empty, Loading, DatePipe, FormsModule, RouterLink],
  selector: 'app-members',
  styleUrl: './members.scss',
  templateUrl: './members.html',
})
export class Members {
  private readonly members = inject(MemberAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  /** Injected rather than reached for globally, so the invitation link's origin is SSR-safe. */
  private readonly document = inject(DOCUMENT);

  protected readonly rows = signal<Member[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** The state the rows on screen were ASKED for — mirrored from the URL, never set directly. */
  protected readonly filter = signal<StatusFilter>(null);
  protected readonly query = signal('');
  protected readonly page = signal(1);

  /** From the page envelope: how many match in all, and how many a page holds. */
  protected readonly total = signal(0);
  protected readonly pageSize = signal(MEMBERS_PAGE_SIZE);

  /**
   * What is in the search box, which runs AHEAD of `query` by up to one debounce pause. Two signals,
   * because the box must never be overwritten mid-typing by the URL it is about to change.
   */
  protected readonly searchInput = signal('');

  /** The pending debounce, cleared on every keystroke and on destroy. */
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Rows with a mutation in flight, so one slow row does not disable the whole list. */
  protected readonly busy = createBusySet();

  /** Id of the row whose action failed. Cleared when that row is retried. */
  protected readonly failedId = signal<string | null>(null);

  /**
   * Every message this screen produces goes through the toast (S-19, outlet 3).
   *
   * It used to be a `.notice` banner pinned above the list. A row action is taken from a row and
   * reported the moment it finishes, with the list still in front of the admin — outlet 3 exactly.
   * The banner also had to be cleared by hand at the top of five different methods, which is how a
   * stale "kod unieważniony" could outlive the row it described.
   */
  private readonly toast = inject(ToastService);

  /** Id of the row whose action menu is open, or null. At most one is ever open. */
  protected readonly openMenuId = signal<string | null>(null);

  // --- member code ----------------------------------------------------------

  /**
   * The row whose code panel is open, and the code itself. Two signals rather than one object so
   * the panel can render "sprawdzam…" against the right row before the answer arrives.
   *
   * At most one is ever open, and nothing caches: leaving the panel would drop the code, which is
   * the behaviour worth having for a credential the admin has already written down or read out.
   */
  protected readonly codeMemberId = signal<string | null>(null);
  protected readonly code = signal<AccessCodeView | null>(null);

  /** Set when a lookup came back empty — "there is no code" is an answer, not a failure. */
  protected readonly codeMissing = signal(false);

  /** Clipboard outcome for the open panel. Both cleared whenever the panel changes. */
  protected readonly codeCopied = signal(false);
  protected readonly codeCopyFailed = signal(false);

  /**
   * Incremented on every load. Nothing cancels an in-flight request, so without this the LAST
   * RESPONSE would win rather than the last request: two quick filter clicks can resolve out of
   * order and leave the rows disagreeing with the highlighted chip, silently. A response whose
   * generation is stale is discarded instead of applied.
   */
  private readonly fence = createLoadFence();

  /** The 1-based range the pager reads out, e.g. `26–50 z 312`. */
  protected readonly rangeFrom = computed(() => (this.page() - 1) * this.pageSize() + 1);
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

  /**
   * Every URL change lands here, and this is the only caller of `load()` that is not a retry or a
   * refetch after a refusal. A URL that is not canonical is rewritten first — replacing the entry, so
   * Back does not return to the junk one — and the rewrite's own emission is what loads.
   */
  private async onParams(params: ParamMap): Promise<void> {
    const { state, canonical } = readState(params);

    if (!canonical) {
      await this.navigate(state, true);
      return;
    }

    this.filter.set(state.filter);
    this.query.set(state.q);
    this.page.set(state.page);

    // Only when the URL disagrees with the box: while the admin types, the box is AHEAD of the URL,
    // and overwriting it with the phrase it is about to replace would eat keystrokes (a trailing
    // space, at the very least, which the URL never carries).
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

    try {
      const page = this.page();
      const result = await this.members.getMembers({
        filter: this.filter() ?? undefined,
        search: this.query() || undefined,

        // Page 1 is the API's default, so it is left off — the request for the first page looks the
        // same however the admin arrived at it.
        page: page > 1 ? page : undefined,
        pageSize: MEMBERS_PAGE_SIZE,
      });

      // A newer load started while this one was in flight — its answer is the current one, so drop
      // ours rather than overwriting fresher rows with staler ones.
      if (!this.fence.isCurrent(generation)) {
        return;
      }

      // The page ran out from under the URL — a block under the Aktywni filter, a bookmark from when
      // the club was bigger. "Brak członków" would be a lie, so go to the last page that exists
      // instead; replacing, so Back does not return to the empty one.
      if (result.items.length === 0 && page > 1 && result.total > 0) {
        const last = Math.ceil(result.total / result.pageSize);
        await this.navigate({ q: this.query(), filter: this.filter(), page: last }, true);
        return;
      }

      this.rows.set(result.items);
      this.total.set(result.total);
      this.pageSize.set(result.pageSize);
    } catch {
      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.loadFailed.set(true);
    } finally {
      // Only the newest load owns the spinner; an older one finishing must not clear it while the
      // newer request is still running.
      if (this.fence.isCurrent(generation)) {
        this.loading.set(false);
      }
    }
  }

  /**
   * A chip NAVIGATES; the URL change is what loads. Back to page 1, because page 3 of one filter says
   * nothing about page 3 of another — and a phrase still waiting out its debounce goes along, so the
   * chip does not race the search box's own navigation.
   */
  protected async setFilter(next: StatusFilter): Promise<void> {
    if (this.filter() === next) {
      return;
    }

    this.failedId.set(null);

    // The panel belongs to a row that may not survive the new filter, and a code left floating over
    // a list it no longer matches is worse than one the admin has to reveal again.
    this.closeCode();
    this.cancelSearch();
    await this.navigate(
      { q: this.searchInput().trim().slice(0, MAX_SEARCH_LENGTH), filter: next, page: 1 },
      false,
    );
  }

  /** The search box's every keystroke. Searches only once typing pauses. */
  protected onSearchInput(value: string): void {
    this.searchInput.set(value);
    this.cancelSearch();

    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      void this.commitSearch();
    }, SEARCH_DEBOUNCE_MS);
  }

  /**
   * Replaces the history entry rather than pushing one: a phrase typed a letter at a time would
   * otherwise leave a Back press per pause. And back to page 1 — a new phrase is a new list.
   */
  private async commitSearch(): Promise<void> {
    const q = this.searchInput().trim().slice(0, MAX_SEARCH_LENGTH);
    if (q === this.query()) {
      return;
    }

    await this.navigate({ q, filter: this.filter(), page: 1 }, true);
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

  /** Pushes, unlike search: a page the admin chose is a place Back should return to. */
  private async goToPage(page: number): Promise<void> {
    this.closeMenu();
    this.closeCode();
    this.failedId.set(null);
    await this.navigate({ q: this.query(), filter: this.filter(), page }, false);
  }

  /** Writes the state to the URL. Defaults are left off, so the plain list is plain `/admin/members`. */
  private async navigate(state: ListState, replaceUrl: boolean): Promise<void> {
    await this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        q: state.q || null,
        filter: state.filter,
        page: state.page > 1 ? state.page : null,
      },
      replaceUrl,
    });
  }

  protected async block(member: Member): Promise<void> {
    await this.mutate(
      member,
      () => this.members.block(member.id),
      (row) => ({
        ...row,
        membershipStatus: 'Blocked',

        // Both, because the API moves both: a person barred from the club whose login still worked
        // would reach every screen the ActiveMember policy guards.
        accountStatus: row.userId ? 'Blocked' : null,
      }),
      blockFailureMessage,
    );
  }

  protected async unblock(member: Member): Promise<void> {
    await this.mutate(
      member,
      () => this.members.unblock(member.id),
      (row) => ({
        ...row,
        membershipStatus: 'Active',
        accountStatus: row.userId ? 'Active' : null,
      }),
      // One reason, and it is the stale-list conflict — see unblock-failure.ts for why the union
      // still gets a table rather than being treated as the exception.
      unblockFailureMessage,
    );
  }

  /** Whether the row holds a role. Names are compared as stored — see Member.roles. */
  protected hasRole(member: Member, role: string): boolean {
    return member.roles.includes(role);
  }

  protected isAdmin(member: Member): boolean {
    return this.hasRole(member, ROLES.admin);
  }

  protected isTrainer(member: Member): boolean {
    return this.hasRole(member, ROLES.trainer);
  }

  /**
   * Roles worth a badge. `User` is excluded because every member has it — a badge every row carries
   * distinguishes nothing and only crowds the row on a phone.
   */
  protected notableRoles(member: Member): string[] {
    return member.roles.filter((role) => role !== ROLES.member);
  }

  protected roleLabel(role: string): string {
    switch (role) {
      case ROLES.admin:
        return 'Administrator';
      case ROLES.trainer:
        return 'Trener';
      default:
        return role;
    }
  }

  /**
   * The role action exists only on active accounts, mirroring the API's not_active guard. Offering
   * it elsewhere would put a button on the screen whose only outcome is a 409.
   */
  protected canChangeTrainer(member: Member): boolean {
    return member.accountStatus === 'Active' && member.membershipStatus === 'Active';
  }

  /** A record the club keeps for someone who never registered (S-14). */
  protected hasNoAccount(member: Member): boolean {
    return member.userId === null;
  }

  protected canBlock(member: Member): boolean {
    return member.membershipStatus !== 'Blocked' && !this.isAdmin(member);
  }

  protected canUnblock(member: Member): boolean {
    return member.membershipStatus === 'Blocked';
  }

  /**
   * What the row's badge says. One label rather than two, because two statuses side by side on a
   * phone row is noise: the membership answers "may they use the club", and the account status only
   * adds anything while it DISAGREES with it.
   *
   * <p>
   * Since S-16 that disagreement has one shape left — a blocked person — so the pending branch is
   * gone. `accountStatus` is still read, because a row could in principle still carry the retired
   * value from an old database, and falling through to "Aktywny" for it would be a claim rather than
   * a reading.
   * </p>
   */
  protected statusLabel(member: Member): string {
    if (member.membershipStatus === 'Blocked') {
      return 'Zablokowany';
    }

    return member.userId ? 'Aktywny' : 'Bez konta';
  }

  /** Drives the badge's colour. Kept separate from the label so the CSS never parses Polish. */
  protected statusKind(member: Member): string {
    if (member.membershipStatus === 'Blocked') {
      return 'blocked';
    }

    return member.userId ? 'active' : 'no-account';
  }

  /**
   * Grant or revoke in one action, because the row already tells the admin which way it will go.
   * Patches `roles` in place on success, exactly as the status actions patch `status`.
   */
  protected async toggleTrainer(member: Member): Promise<void> {
    const held = this.isTrainer(member);
    this.closeMenu();

    await this.mutate(
      member,
      () => (held ? this.members.revokeTrainer(member.id) : this.members.grantTrainer(member.id)),
      (row) => ({
        ...row,
        roles: held
          ? row.roles.filter((name) => name !== ROLES.trainer)
          : [...row.roles, ROLES.trainer],
      }),
      trainerRoleFailureMessage,
    );
  }

  // --- member code ----------------------------------------------------------

  /**
   * The code exists to attach a NEW account to this record, so it is offered only where that is
   * possible: no login yet, and a membership that is not blocked. Issuing one for a blocked member
   * would produce a code that registration refuses anyway.
   */
  protected canIssueCode(member: Member): boolean {
    return this.hasNoAccount(member) && member.membershipStatus === 'Active';
  }

  /** Reveals the outstanding code. A 204 (none, or expired) is reported as "no code", not an error. */
  protected async showCode(member: Member): Promise<void> {
    this.closeMenu();
    await this.withCodePanel(member, () => this.members.getAccessCode(member.id));
  }

  /** Issues a fresh code, replacing any outstanding one, and shows it. */
  protected async issueCode(member: Member): Promise<void> {
    this.closeMenu();
    await this.withCodePanel(member, () => this.members.issueAccessCode(member.id));
  }

  /**
   * Kills the outstanding code and closes the panel. The row's `hasAccessCode` is patched in place,
   * exactly as the status actions patch their fields.
   */
  protected async revokeCode(member: Member): Promise<void> {
    this.closeMenu();
    this.closeCode();
    this.failedId.set(null);
    this.busy.setBusy(member.id, true);

    try {
      await this.members.revokeAccessCode(member.id);
      this.patchRow(member.id, (row) => ({ ...row, hasAccessCode: false }));
      this.toast.success(`Kod dla ${member.displayName} został unieważniony.`);
    } catch (failure) {
      await this.handleCodeFailure(member, failure);
    } finally {
      this.busy.setBusy(member.id, false);
    }
  }

  /**
   * Copies the code as it is displayed. The Clipboard API needs a secure context and a permission
   * that can simply be refused, so the failure is SHOWN rather than swallowed — an admin who thinks
   * they copied a code and pastes something else is the outcome worth avoiding, and the code stays
   * on screen to be typed out.
   */
  protected async copyCode(): Promise<void> {
    const view = this.code();
    if (!view) {
      return;
    }

    this.codeCopied.set(false);
    this.codeCopyFailed.set(false);

    try {
      await navigator.clipboard.writeText(view.code);
      this.codeCopied.set(true);
    } catch {
      this.codeCopyFailed.set(true);
    }
  }

  /**
   * Copies the whole invitation URL rather than the bare code (S-17, IR-06).
   *
   * <p>
   * BOTH ACTIONS EXIST BECAUSE BOTH DELIVERIES DO. The link is what an admin pastes into a message;
   * the code is what they read down the phone, which is the entire reason its alphabet drops the
   * characters people confuse when transcribing. Replacing the code with the link would take that
   * away.
   * </p>
   *
   * <p>
   * Built from the document's OWN origin, so it is correct on localhost, on a staging host and in
   * production without a configured base URL to keep in step. The code goes in as displayed — the
   * dash is in the API's own alphabet-normalising path, so a link carrying it resolves fine.
   * </p>
   */
  protected async copyInvitationLink(): Promise<void> {
    const view = this.code();
    if (!view) {
      return;
    }

    this.codeCopied.set(false);
    this.codeCopyFailed.set(false);

    // `invitationCode`, not `memberCode`: the query parameter and the API field are deliberately
    // named differently (M-5 charter). This is the name the register screen reads.
    const url = `${this.document.location.origin}/register?invitationCode=${encodeURIComponent(view.code)}`;

    try {
      await navigator.clipboard.writeText(url);
      this.codeCopied.set(true);
    } catch {
      this.codeCopyFailed.set(true);
    }
  }

  protected closeCode(): void {
    this.codeMemberId.set(null);
    this.code.set(null);
    this.codeMissing.set(false);
    this.codeCopied.set(false);
    this.codeCopyFailed.set(false);
  }

  /**
   * Shared shape for reveal and issue: open the panel on this row, run the request, show what came
   * back. One method for both because the only difference is the request — including what happens on
   * an empty answer, since a reveal that finds nothing means the row's `hasAccessCode` was already
   * wrong and should be corrected rather than trusted.
   */
  private async withCodePanel(
    member: Member,
    action: () => Promise<AccessCodeView | null>,
  ): Promise<void> {
    this.closeCode();
    this.failedId.set(null);
    this.codeMemberId.set(member.id);
    this.busy.setBusy(member.id, true);

    try {
      const view = await action();

      this.code.set(view);
      this.codeMissing.set(view === null);
      this.patchRow(member.id, (row) => ({ ...row, hasAccessCode: view !== null }));
    } catch (failure) {
      this.closeCode();
      await this.handleCodeFailure(member, failure);
    } finally {
      this.busy.setBusy(member.id, false);
    }
  }

  /**
   * 409 means the list is stale — the member registered in the meantime (`has_account`), was blocked
   * underneath us (`member_blocked`), or somebody else changed the row — so it is refetched rather
   * than patched, as everywhere else here.
   */
  private async handleCodeFailure(member: Member, failure: unknown): Promise<void> {
    const info = classifyFailure(failure);

    // A 429, a 500 or a dead network is not a stale list, and refetching would only fail again.
    const transport = transportMessage(info);
    if (transport !== null) {
      this.toast.error(transport);
      this.failedId.set(member.id);
      return;
    }

    if (info.status === 409) {
      this.announce(info.reason, accessCodeFailureMessage(info.reason));
      await this.load();
      return;
    }

    this.failedId.set(member.id);
  }

  // --- row menu -------------------------------------------------------------
  //
  // The first menu in this SPA; nothing else here had one, so open/close, outside-click and keyboard
  // handling are all built here rather than reused.

  protected toggleMenu(member: Member): void {
    this.openMenuId.update((current) => (current === member.id ? null : member.id));
  }

  protected closeMenu(): void {
    this.openMenuId.set(null);
  }

  /**
   * Any click outside an open menu dismisses it — the conventional behaviour for a popup.
   *
   * The inside/outside test is done HERE, by inspecting the click's target, rather than by having
   * the menu stop propagation in the template. A stopPropagation handler would have to sit on a
   * plain div, which is a non-focusable element with an interaction handler — exactly what the
   * accessibility lint rules forbid, and for good reason.
   */
  @HostListener('document:click', ['$event'])
  protected onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement | null;

    // Covers the trigger too: it lives inside .row-menu, so the click that opens a menu is not also
    // read as a click outside it.
    if (target?.closest('.row-menu')) {
      return;
    }

    this.closeMenu();
  }

  /**
   * Escape closes and returns focus to the trigger. Losing focus to the document body would strand
   * a keyboard user at the top of the page.
   */
  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    const id = this.openMenuId();
    if (!id) {
      return;
    }

    this.closeMenu();
    document.getElementById(triggerId(id))?.focus();
  }

  /** Arrow keys move between entries; Home/End jump to the ends. */
  protected onMenuKeydown(event: KeyboardEvent): void {
    const keys = ['ArrowDown', 'ArrowUp', 'Home', 'End'];
    if (!keys.includes(event.key)) {
      return;
    }

    const menu = event.currentTarget as HTMLElement;
    const items = Array.from(menu.querySelectorAll<HTMLElement>('[role="menuitem"]'));
    if (items.length === 0) {
      return;
    }

    event.preventDefault();

    const current = items.indexOf(document.activeElement as HTMLElement);
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? items.length - 1
          : event.key === 'ArrowDown'
            ? (current + 1 + items.length) % items.length
            : (current - 1 + items.length) % items.length;

    items[next]?.focus();
  }

  protected triggerIdFor(id: string): string {
    return triggerId(id);
  }

  /**
   * Shared shape for all three actions.
   *
   * The row is updated IN PLACE on success, never removed: unlike approve on the approvals queue,
   * the member still belongs on this list — with a different badge. Removing it would tell the admin
   * the member vanished.
   *
   * On 409 the local view is stale (or the action was refused), so the list is refetched rather than
   * patched: guessing what the row became is how a screen ends up lying about state it never saw.
   */
  private async mutate(
    member: Member,
    action: () => Promise<void>,
    patch: (row: Member) => Member,
    message: (reason: unknown) => string,
  ): Promise<void> {
    const generation = this.fence.current();

    this.failedId.set(null);
    this.busy.setBusy(member.id, true);

    try {
      await action();

      // The list was reloaded while this mutation was in flight, so the rows we would patch are no
      // longer the rows we acted on — the member may not even be in the current filter. Patching
      // would silently no-op and make a successful action look like it did nothing; refetch instead.
      if (!this.fence.isCurrent(generation)) {
        await this.load();
        return;
      }

      this.patchRow(member.id, patch);
    } catch (failure) {
      const info = classifyFailure(failure);

      const transport = transportMessage(info);
      if (transport !== null) {
        this.toast.error(transport);
        this.failedId.set(member.id);
        return;
      }

      if (info.status === 409) {
        this.announce(info.reason, message(info.reason));
        await this.load();
        return;
      }

      // The row keeps its CURRENT status. Showing the intended one would tell the admin an action
      // succeeded when it did not, and nothing else in the product would correct that belief.
      this.failedId.set(member.id);
    } finally {
      this.busy.setBusy(member.id, false);
    }
  }

  /**
   * Reports a 409 in the right TONE.
   *
   * All of these refetch, but they do not all mean the same thing. `conflict` and `failed` say
   * nothing was wrong except the timing — the list had moved, and it has just been reloaded — which
   * is neither a success nor a failure, and is precisely what `info` exists for. Everything else
   * names a rule the admin has to do something about, and that is an error.
   */
  private announce(reason: string | undefined, message: string): void {
    if (reason === undefined || reason === 'conflict' || reason === 'failed') {
      this.toast.info(message);
      return;
    }

    this.toast.error(message);
  }

  /** Replaces one row in place. Never removes it — see `mutate`. */
  private patchRow(id: string, patch: (row: Member) => Member): void {
    this.rows.update((rows) => rows.map((row) => (row.id === id ? patch(row) : row)));
  }
}
