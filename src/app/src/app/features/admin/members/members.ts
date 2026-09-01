import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import {
  BlockFailure,
  Member,
  MemberStatus,
  UnblockFailure,
} from '../../../core/admin/member-admin.models';

/** The filter positions, including "everyone". `null` means no status parameter is sent. */
type StatusFilter = MemberStatus | null;

/**
 * The admin's member list (FR-004, FR-005): everyone, filterable by status, searchable by name or
 * email, with block and unblock per row.
 *
 * Split of work between server and client is deliberate (S-02 planning): the STATUS filter refetches
 * because it maps onto the indexed query the API already has, while SEARCH filters the loaded rows,
 * because a club's list fits on one screen and a request per keystroke would need debouncing to buy
 * nothing.
 *
 * The approvals screen (S-01) stays as it is. Approve appears here too, on pending rows, so that a
 * pending member found through this screen is actionable where they are found rather than sending
 * the admin somewhere else to do the obvious thing.
 */
@Component({
  imports: [DatePipe, FormsModule],
  selector: 'app-members',
  styleUrl: './members.scss',
  templateUrl: './members.html',
})
export class Members implements OnInit {
  private readonly members = inject(MemberAdminService);

  protected readonly rows = signal<Member[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  protected readonly filter = signal<StatusFilter>(null);
  protected readonly search = signal('');

  /** Ids with a mutation in flight, so one slow row does not disable the whole list. */
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  /** Id of the row whose action failed. Cleared when that row is retried. */
  protected readonly failedId = signal<string | null>(null);

  /** A list-level message — a stale row, or a refused action that retrying cannot fix. */
  protected readonly notice = signal<string | null>(null);

  /**
   * Incremented on every load. Nothing cancels an in-flight request, so without this the LAST
   * RESPONSE would win rather than the last request: two quick filter clicks can resolve out of
   * order and leave the rows disagreeing with the highlighted chip, silently. A response whose
   * generation is stale is discarded instead of applied.
   */
  private generation = 0;

  /**
   * Search runs here rather than at the API. Matches display name or email, case-insensitively;
   * both are what an admin actually has to hand when looking someone up.
   */
  protected readonly visible = computed(() => {
    const term = this.search().trim().toLocaleLowerCase();
    if (!term) {
      return this.rows();
    }

    return this.rows().filter(
      (row) =>
        row.displayName.toLocaleLowerCase().includes(term) ||
        row.email.toLocaleLowerCase().includes(term),
    );
  });

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = ++this.generation;

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const rows = await this.members.getMembers(this.filter() ?? undefined);

      // A newer load started while this one was in flight — its answer is the current one, so drop
      // ours rather than overwriting fresher rows with staler ones.
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
      // Only the newest load owns the spinner; an older one finishing must not clear it while the
      // newer request is still running.
      if (generation === this.generation) {
        this.loading.set(false);
      }
    }
  }

  /** A status filter change is a different query, so it refetches. Search never does. */
  protected async setFilter(next: StatusFilter): Promise<void> {
    if (this.filter() === next) {
      return;
    }

    this.filter.set(next);
    this.notice.set(null);
    this.failedId.set(null);
    await this.load();
  }

  protected async approve(member: Member): Promise<void> {
    await this.mutate(
      member,
      () => this.members.approve(member.id),
      'Active',
      () => `Nie udało się zatwierdzić — ${member.displayName} nie oczekuje już na zatwierdzenie.`,
    );
  }

  protected async block(member: Member): Promise<void> {
    await this.mutate(
      member,
      () => this.members.block(member.id),
      'Blocked',
      (reason) =>
        reason === 'is_admin'
          ? `${member.displayName} zarządza klubem i nie może zostać zablokowany.`
          : null,
    );
  }

  protected async unblock(member: Member): Promise<void> {
    await this.mutate(
      member,
      () => this.members.unblock(member.id),
      'Active',
      (reason) =>
        reason === 'not_blocked'
          ? `${member.displayName} nie jest zablokowany — użyj „Zatwierdź”.`
          : null,
    );
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
    becomes: MemberStatus,
    explain: (reason: string | undefined) => string | null,
  ): Promise<void> {
    const generation = this.generation;

    this.failedId.set(null);
    this.notice.set(null);
    this.setBusy(member.id, true);

    try {
      await action();

      // The list was reloaded while this mutation was in flight, so the rows we would patch are no
      // longer the rows we acted on — the member may not even be in the current filter. Patching
      // would silently no-op and make a successful action look like it did nothing; refetch instead.
      if (generation !== this.generation) {
        await this.load();
        return;
      }

      this.rows.update((rows) =>
        rows.map((row) => (row.id === member.id ? { ...row, status: becomes } : row)),
      );
    } catch (failure) {
      const response = failure as HttpErrorResponse;

      if (response?.status === 409) {
        const reason = (response.error as BlockFailure | UnblockFailure | undefined)?.reason;
        this.notice.set(explain(reason) ?? 'Lista była nieaktualna — odświeżono.');
        await this.load();
        return;
      }

      // The row keeps its CURRENT status. Showing the intended one would tell the admin an action
      // succeeded when it did not, and nothing else in the product would correct that belief.
      this.failedId.set(member.id);
    } finally {
      this.setBusy(member.id, false);
    }
  }

  protected isBusy(id: string): boolean {
    return this.busy().has(id);
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
