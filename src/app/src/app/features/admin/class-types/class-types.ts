import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ClassTypeService } from '../../../core/scheduling/class-type.service';
import { ClassTypeFailure, ClassTypeSummary } from '../../../core/scheduling/class-type.models';

/**
 * The admin's class-type definitions (prd-v2 FR-005, FR-006).
 *
 * Same shape as the classes screen: loading / failed / empty signals, a per-row busy Set so one slow
 * row does not disable the list, and a generation guard so a refetch that resolves late cannot
 * overwrite fresher rows.
 *
 * NO DELETE. FR-006 replaces deletion with deactivation, so there is no confirmation prompt to
 * write: deactivating is reversible, which is the whole reason it replaced deleting.
 *
 * The API returns active AND inactive in one call, so the "show inactive" toggle is a pure client
 * filter — flicking it costs no round trip, and the admin can reactivate something they can see.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-class-types',
  styleUrl: './class-types.scss',
  templateUrl: './class-types.html',
})
export class ClassTypes implements OnInit {
  private readonly classTypes = inject(ClassTypeService);

  /** Everything the API returned, unfiltered. `visible` is what the template renders. */
  protected readonly rows = signal<ClassTypeSummary[]>([]);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** Off by default: retired types are the exception and should not crowd the working list. */
  protected readonly showInactive = signal(false);

  protected readonly visible = computed(() =>
    this.showInactive() ? this.rows() : this.rows().filter((t) => t.isActive),
  );

  /** True when types exist but the filter hides every one — a different message from "none yet". */
  protected readonly hiddenByFilter = computed(
    () => this.rows().length > 0 && this.visible().length === 0,
  );

  /** Ids with an action in flight. */
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  /** Id of the row whose action failed. Cleared when another action starts. */
  protected readonly failedId = signal<string | null>(null);

  /** A list-level message — a refusal that retrying cannot fix. */
  protected readonly notice = signal<string | null>(null);

  /** See members.ts — nothing cancels an in-flight request, so the last RESPONSE would otherwise win. */
  private generation = 0;

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = ++this.generation;

    this.loading.set(true);
    this.loadFailed.set(false);

    // A message about rows that are about to be replaced does not survive them. Without this, a
    // retry after a failed activation renders the old name_taken notice above a fresh list.
    this.notice.set(null);
    this.failedId.set(null);

    try {
      const rows = await this.classTypes.getAll();
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

  protected toggleInactive(): void {
    this.notice.set(null);
    this.failedId.set(null);
    this.showInactive.update((shown) => !shown);
  }

  protected async deactivate(row: ClassTypeSummary): Promise<void> {
    await this.setActive(row, false);
  }

  protected async activate(row: ClassTypeSummary): Promise<void> {
    await this.setActive(row, true);
  }

  /**
   * Flips one row's activation and patches it in place from the response, rather than refetching:
   * the server returns the updated type, so a second round trip would buy nothing and would reorder
   * the list under the admin's cursor.
   */
  private async setActive(row: ClassTypeSummary, active: boolean): Promise<void> {
    this.failedId.set(null);
    this.notice.set(null);
    this.setBusy(row.id, true);

    try {
      const updated = active
        ? await this.classTypes.activate(row.id)
        : await this.classTypes.deactivate(row.id);

      this.rows.update((rows) => rows.map((t) => (t.id === updated.id ? updated : t)));

      // Deactivating while the filter is off makes the row vanish, and absence is a poor
      // confirmation — the admin cannot tell it from a failed request. Say what happened.
      this.notice.set(
        active
          ? `Typ „${updated.name}” jest znowu aktywny.`
          : `Typ „${updated.name}” został dezaktywowany. Zaznacz „Pokaż nieaktywne”, aby go zobaczyć.`,
      );
    } catch (failure) {
      const reason = ((failure as HttpErrorResponse)?.error as ClassTypeFailure | undefined)
        ?.reason;

      // Activation is the one action that can be refused for a reason the admin can actually fix,
      // and it has no control to attach the message to — the request carries no name. Deactivating
      // released this name, and another type has claimed it since.
      if (reason === 'name_taken') {
        this.notice.set(
          `Nazwa „${row.name}” jest teraz zajęta przez inny aktywny typ. ` +
            'Zmień nazwę tamtego typu albo go dezaktywuj, zanim przywrócisz ten.',
        );
        return;
      }

      this.failedId.set(row.id);
    } finally {
      this.setBusy(row.id, false);
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
