import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { useScreenTitle } from '../../../core/layout/screen-title';
import { ExerciseService } from '../../../core/training/exercise.service';
import { ExerciseSummary } from '../../../core/training/exercise.models';
import { classifyFailure } from '../../../core/http/failure';
import { Loading } from '../../../shared/forms/loading/loading';
import { Empty } from '../../../shared/forms/empty/empty';
import { ExerciseView } from '../../../shared/exercise-view/exercise-view';

/**
 * One exercise, laid out for reading (prd.md FR-018, FR-019).
 *
 * THE FIRST READ-ONLY DETAIL SCREEN IN THIS APP. Every other admin feature is a list plus a form,
 * and reading eight prose fields as form values is exactly what this exists to avoid. It is also the
 * layout S-11 will adapt for the member's view of an exercise inside their plan.
 *
 * A section that is absent is OMITTED, not rendered as an em dash: an exercise with a name and
 * nothing else is a legitimate entry (FR-018 makes every other field optional), and eight empty
 * headings would make it look broken.
 */
@Component({
  imports: [Empty, ExerciseView, Loading, RouterLink],
  selector: 'app-exercise-detail',
  styleUrl: './exercise-detail.scss',
  templateUrl: './exercise-detail.html',
})
export class ExerciseDetail implements OnInit {
  private readonly exercises = inject(ExerciseService);
  private readonly route = inject(ActivatedRoute);

  protected readonly exercise = signal<ExerciseSummary | null>(null);

  // The bar names the screen the way its h1 does (mobile-native-feel).
  protected readonly screenTitleEffect = useScreenTitle(() => this.exercise()?.name ?? null);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** A 404 is its own state: the id is wrong, so retrying the same request cannot help. */
  protected readonly notFound = signal(false);

  private id = '';

  async ngOnInit(): Promise<void> {
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.notFound.set(false);

    try {
      this.exercise.set(await this.exercises.getById(this.id));
    } catch (failure) {
      // Outlet 4 — a screen that could not be populated at all. `notFound` is now a KIND rather
      // than a hand-written status test, so a 404 here reads the same way it does everywhere else.
      if (classifyFailure(failure).kind === 'notFound') {
        this.notFound.set(true);
      } else {
        this.loadFailed.set(true);
      }
    } finally {
      this.loading.set(false);
    }
  }

  /** The edit screen for this exercise — the whole point of a read-only view having a way out. */
  protected editLink(): unknown[] {
    return ['/admin/exercises', this.id, 'edit'];
  }
}
