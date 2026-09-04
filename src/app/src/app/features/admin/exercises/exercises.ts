import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ExerciseService } from '../../../core/training/exercise.service';
import { ExerciseFailure, ExerciseSummary } from '../../../core/training/exercise.models';
import { isVideoId, thumbnailUrl } from '../../../core/training/youtube';

/**
 * The admin's exercise library (prd.md FR-018, FR-019).
 *
 * Same shape as the class-types screen: loading / failed / empty signals, a per-row busy Set so one
 * slow row does not disable the list, and a generation guard so a refetch that resolves late cannot
 * overwrite fresher rows.
 *
 * NO DELETE. Deactivation replaces deletion, so there is no confirmation prompt to write —
 * deactivating is reversible, which is the whole reason it replaced deleting. It matters more here
 * than for class types: S-11's training plans will reference these rows.
 *
 * The API returns active AND inactive in one call, so the "show inactive" toggle is a pure client
 * filter — flicking it costs no round trip, and the admin can reactivate something they can see.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-exercises',
  styleUrl: './exercises.scss',
  templateUrl: './exercises.html',
})
export class Exercises implements OnInit {
  private readonly exercises = inject(ExerciseService);

  /** Everything the API returned, unfiltered. `visible` is what the template renders. */
  protected readonly rows = signal<ExerciseSummary[]>([]);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** Off by default: retired exercises are the exception and should not crowd the working list. */
  protected readonly showInactive = signal(false);

  protected readonly visible = computed(() =>
    this.showInactive() ? this.rows() : this.rows().filter((e) => e.isActive),
  );

  /** True when exercises exist but the filter hides every one — a different message from "none yet". */
  protected readonly hiddenByFilter = computed(
    () => this.rows().length > 0 && this.visible().length === 0,
  );

  /** Ids with an action in flight. */
  protected readonly busy = signal<ReadonlySet<string>>(new Set());

  /** Id of the row whose action failed. Cleared when another action starts. */
  protected readonly failedId = signal<string | null>(null);

  /** A list-level message — a refusal that retrying cannot fix. */
  protected readonly notice = signal<string | null>(null);

  /**
   * Ids whose thumbnail failed to load. YouTube serves these, so a 404 (deleted video) or an offline
   * moment is outside our control — the placeholder takes over rather than leaving a broken image.
   */
  protected readonly brokenThumbnails = signal<ReadonlySet<string>>(new Set());

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
      const rows = await this.exercises.getAll();
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

  /** The thumbnail URL, or null when this row has no usable video or its image already failed. */
  protected thumbnail(row: ExerciseSummary): string | null {
    if (!isVideoId(row.videoId) || this.brokenThumbnails().has(row.id)) {
      return null;
    }

    return thumbnailUrl(row.videoId);
  }

  protected onThumbnailError(row: ExerciseSummary): void {
    this.brokenThumbnails.update((ids) => new Set(ids).add(row.id));
  }

  protected async deactivate(row: ExerciseSummary): Promise<void> {
    await this.setActive(row, false);
  }

  protected async activate(row: ExerciseSummary): Promise<void> {
    await this.setActive(row, true);
  }

  /**
   * Flips one row's activation and patches it in place from the response, rather than refetching:
   * the server returns the updated exercise, so a second round trip would buy nothing and would
   * reorder the list under the admin's cursor.
   */
  private async setActive(row: ExerciseSummary, active: boolean): Promise<void> {
    this.failedId.set(null);
    this.notice.set(null);
    this.setBusy(row.id, true);

    try {
      const updated = active
        ? await this.exercises.activate(row.id)
        : await this.exercises.deactivate(row.id);

      this.rows.update((rows) => rows.map((e) => (e.id === updated.id ? updated : e)));

      // Deactivating while the filter is off makes the row vanish, and absence is a poor
      // confirmation — the admin cannot tell it from a failed request. Say what happened.
      this.notice.set(
        active
          ? `Ćwiczenie „${updated.name}” jest znowu aktywne.`
          : `Ćwiczenie „${updated.name}” zostało dezaktywowane. Zaznacz „Pokaż nieaktywne”, aby je zobaczyć.`,
      );
    } catch (failure) {
      const reason = ((failure as HttpErrorResponse)?.error as ExerciseFailure | undefined)?.reason;

      // Activation is the one action that can be refused for a reason the admin can actually fix,
      // and it has no control to attach the message to — the request carries no name. Deactivating
      // released this name, and another exercise has claimed it since.
      if (reason === 'name_taken') {
        this.notice.set(
          `Nazwa „${row.name}” jest teraz zajęta przez inne aktywne ćwiczenie. ` +
            'Zmień nazwę tamtego ćwiczenia albo je dezaktywuj, zanim przywrócisz to.',
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
