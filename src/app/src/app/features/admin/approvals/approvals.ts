import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import { PendingMember } from '../../../core/admin/member-admin.models';

/**
 * The admin's approval queue (FR-003, FR-005) — the screen that closes S-01's loop, and the only
 * thing standing between a registration and a member who can book.
 *
 * Approve is the whole surface. No reject (FR-003 dropped it from the MVP), no block/unblock and no
 * full member list — S-02 owns those and is blocked on the PRD's open question about what happens to
 * a blocked member's existing bookings.
 */
@Component({
  imports: [DatePipe],
  selector: 'app-approvals',
  styleUrl: './approvals.scss',
  templateUrl: './approvals.html',
})
export class Approvals implements OnInit {
  private readonly members = inject(MemberAdminService);

  protected readonly pending = signal<PendingMember[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** Ids currently being approved, so one slow row does not disable the whole list. */
  protected readonly approving = signal<ReadonlySet<string>>(new Set());

  /** Id of the row whose approve failed. Cleared when that row is retried. */
  protected readonly failedId = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      this.pending.set(await this.members.getPending());
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected async approve(member: PendingMember): Promise<void> {
    this.failedId.set(null);
    this.setApproving(member.id, true);

    try {
      await this.members.approve(member.id);

      // Remove the row locally rather than refetching: the list is small, the answer is already
      // known, and a refetch would make an approval feel slower than it is.
      this.pending.update((rows) => rows.filter((row) => row.id !== member.id));
    } catch {
      // The row STAYS. Dropping it on failure would tell the admin someone was approved when they
      // were not — and nothing else in the product would ever correct that belief.
      this.failedId.set(member.id);
    } finally {
      this.setApproving(member.id, false);
    }
  }

  protected isApproving(id: string): boolean {
    return this.approving().has(id);
  }

  private setApproving(id: string, value: boolean): void {
    this.approving.update((ids) => {
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
