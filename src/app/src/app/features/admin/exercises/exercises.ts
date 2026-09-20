import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ExerciseService } from '../../../core/training/exercise.service';
import { ExerciseSummary } from '../../../core/training/exercise.models';
import { isVideoId, thumbnailUrl } from '../../../core/training/youtube';
import { exerciseFailureMessage } from '../../../core/training/exercise-failure';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { ToastService } from '../../../shared/toast/toast.service';
import { createBusySet } from '../../../shared/forms/busy-set';
import { createLoadFence } from '../../../shared/forms/load-fence';
import { Checkbox } from '../../../shared/forms/checkbox/checkbox';
import { Loading } from '../../../shared/forms/loading/loading';
import { Empty } from '../../../shared/forms/empty/empty';

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
  imports: [Empty, Loading, Checkbox, RouterLink],
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

  /** Rows with a mutation in flight, so one slow row does not disable the whole list. */
  protected readonly busy = createBusySet();

  /** Id of the row whose action failed. Cleared when another action starts. */
  protected readonly failedId = signal<string | null>(null);

  /** A list-level message — a refusal that retrying cannot fix. */
  /**
   * Everything this screen says goes to the toast (S-19, outlet 3).
   *
   * Both messages here answer a ROW action — an activation that took effect, or one the server
   * refused — taken from a list the admin stays looking at. The `.notice` banner it replaces had to
   * be cleared by hand in four separate methods, which is what the comment below used to be about.
   */
  private readonly toast = inject(ToastService);

  /**
   * Ids whose thumbnail failed to load. YouTube serves these, so a 404 (deleted video) or an offline
   * moment is outside our control — the placeholder takes over rather than leaving a broken image.
   */
  protected readonly brokenThumbnails = signal<ReadonlySet<string>>(new Set());

  /** See members.ts — nothing cancels an in-flight request, so the last RESPONSE would otherwise win. */
  private readonly fence = createLoadFence();

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();

    this.loading.set(true);
    this.loadFailed.set(false);

    // A message about rows that are about to be replaced does not survive them. Without this, a
    // retry after a failed activation renders the old name_taken notice above a fresh list.
    this.failedId.set(null);

    try {
      const rows = await this.exercises.getAll();
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

  protected toggleInactive(): void {
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
    this.busy.setBusy(row.id, true);

    try {
      const updated = active
        ? await this.exercises.activate(row.id)
        : await this.exercises.deactivate(row.id);

      this.rows.update((rows) => rows.map((e) => (e.id === updated.id ? updated : e)));

      // Deactivating while the filter is off makes the row vanish, and absence is a poor
      // confirmation — the admin cannot tell it from a failed request. Say what happened.
      this.toast.success(
        active
          ? `Ćwiczenie „${updated.name}” jest znowu aktywne.`
          : `Ćwiczenie „${updated.name}” zostało dezaktywowane. Zaznacz „Pokaż nieaktywne”, aby je zobaczyć.`,
      );
    } catch (failure) {
      const info = classifyFailure(failure);

      // A 429, a 500 or a dead network is not a name clash — it says so itself now.
      const transport = transportMessage(info);
      if (transport !== null) {
        this.toast.error(transport);
        this.failedId.set(row.id);
        return;
      }

      // Activation is the one action that can be refused for a reason the admin can actually fix,
      // and it has no control to attach the message to — the request carries no name. Deactivating
      // released this name, and another exercise has claimed it since.
      if (info.reason === 'name_taken') {
        // THE TABLE'S SENTENCE. This screen used to write its own, longer version for the one
        // refusal that reaches both here and the form — so the same name clash read two ways.
        this.toast.error(exerciseFailureMessage(info.reason));
        return;
      }

      this.failedId.set(row.id);
    } finally {
      this.busy.setBusy(row.id, false);
    }
  }
}
