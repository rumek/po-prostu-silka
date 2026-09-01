import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ClassService } from '../../../core/scheduling/class.service';
import { ScheduledClass } from '../../../core/scheduling/class.models';

/**
 * The admin's class management list (FR-011, FR-012).
 *
 * Same shape as the members screen: loading / failed / empty signals, a per-row busy Set so one slow
 * row does not disable the list, and a generation guard so a refetch that resolves late cannot
 * overwrite fresher rows.
 *
 * Duplicate reports PARTIAL SUCCESS. The API skips weeks whose room is already taken and returns
 * which ones; showing a bare "done" would leave the admin believing in classes that were never
 * created. Delete confirms inline rather than through confirm(), which blocks the event loop and has
 * no precedent in this codebase.
 */
@Component({
  imports: [DatePipe, FormsModule, RouterLink],
  selector: 'app-classes',
  styleUrl: './classes.scss',
  templateUrl: './classes.html',
})
export class Classes implements OnInit {
  private readonly classes = inject(ClassService);

  protected readonly rows = signal<ScheduledClass[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** Ids with an action in flight. */
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  /** Id of the row whose action failed. Cleared when another action starts. */
  protected readonly failedId = signal<string | null>(null);

  /** A list-level message — the duplicate outcome, or a refusal retrying cannot fix. */
  protected readonly notice = signal<string | null>(null);

  /** Which row has its duplicate control open, and for how many weeks. */
  protected readonly duplicatingId = signal<string | null>(null);
  protected readonly weeks = signal(4);

  /** Which row is asking to confirm a delete. */
  protected readonly confirmingDeleteId = signal<string | null>(null);

  /** See members.ts — nothing cancels an in-flight request, so the last RESPONSE would otherwise win. */
  private generation = 0;

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = ++this.generation;

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const rows = await this.classes.getAdminClasses();
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

  protected openDuplicate(id: string): void {
    this.notice.set(null);
    this.failedId.set(null);
    this.confirmingDeleteId.set(null);
    this.duplicatingId.set(this.duplicatingId() === id ? null : id);
  }

  protected async duplicate(row: ScheduledClass): Promise<void> {
    this.failedId.set(null);
    this.notice.set(null);
    this.setBusy(row.id, true);

    try {
      const result = await this.classes.duplicate(row.id, this.weeks());
      this.duplicatingId.set(null);

      // The whole point of the endpoint's contract: say what actually happened, per week.
      this.notice.set(
        result.skippedWeeks.length === 0
          ? `Utworzono ${result.created} ${this.copiesWord(result.created)}.`
          : `Utworzono ${result.created} ${this.copiesWord(result.created)}. ` +
              `Pominięto tydzień ${result.skippedWeeks.join(', ')} — sala jest już zajęta.`,
      );

      await this.load();
    } catch (failure) {
      const reason = ((failure as HttpErrorResponse)?.error as { reason?: string } | undefined)
        ?.reason;

      if (reason === 'invalid_weeks') {
        this.notice.set('Liczba tygodni musi mieścić się w zakresie 1–8.');
        return;
      }

      this.failedId.set(row.id);
    } finally {
      this.setBusy(row.id, false);
    }
  }

  protected confirmDelete(id: string): void {
    this.notice.set(null);
    this.failedId.set(null);
    this.duplicatingId.set(null);
    this.confirmingDeleteId.set(id);
  }

  protected cancelDelete(): void {
    this.confirmingDeleteId.set(null);
  }

  protected async remove(row: ScheduledClass): Promise<void> {
    this.failedId.set(null);
    this.notice.set(null);
    this.setBusy(row.id, true);

    try {
      await this.classes.remove(row.id);
      this.confirmingDeleteId.set(null);

      // Unlike block/unblock on the members screen, a deleted class genuinely leaves the list —
      // removing the row locally is the honest representation, and the list is small.
      this.rows.update((rows) => rows.filter((r) => r.id !== row.id));
    } catch {
      this.failedId.set(row.id);
    } finally {
      this.setBusy(row.id, false);
    }
  }

  protected endsAt(row: ScheduledClass): Date {
    return new Date(new Date(row.startsAt).getTime() + row.durationMinutes * 60_000);
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
