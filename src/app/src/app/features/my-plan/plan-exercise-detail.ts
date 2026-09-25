import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { UpLink } from '../../core/layout/up';
import { useScreenTitle } from '../../core/layout/screen-title';
import { ExerciseSummary } from '../../core/training/exercise.models';
import { TrainingPlanService } from '../../core/training/training-plan.service';
import { classifyFailure } from '../../core/http/failure';
import { Loading } from '../../shared/forms/loading/loading';
import { Empty } from '../../shared/forms/empty/empty';
import { ExerciseView } from '../../shared/exercise-view/exercise-view';

/**
 * One exercise from the member's own plan, with its instructions and video (prd.md FR-020).
 *
 * Adapted from the admin's ExerciseDetail, which was written to be adapted for exactly this. Two
 * differences, both deliberate:
 *
 * <ul>
 *   <li>It reads through <c>/api/plans/mine/exercises/{id}</c>, where the JOIN to the member's active
 *       plan IS the authorization. An exercise not in their plan answers 404 — the "no standalone
 *       library browsing" Non-Goal enforced, not merely respected. So `notFound` is a real state here
 *       and not just a wrong URL.</li>
 *   <li>There is no edit link and no activation badge. A member does not maintain the library, and
 *       an exercise retired after it was prescribed still belongs in their plan.</li>
 * </ul>
 *
 * THE BODY — video, facts, instructions — is shared/exercise-view, the admin's detail screen's too.
 * The video's id is re-checked there immediately before it is trusted, and the spec beside this file
 * still proves a malformed id and a `javascript:` string render no iframe here: moving the check
 * without keeping the test is precisely how it would be lost.
 */
@Component({
  imports: [Empty, ExerciseView, Loading, UpLink],
  selector: 'app-plan-exercise-detail',
  templateUrl: './plan-exercise-detail.html',
})
export class PlanExerciseDetail implements OnInit {
  private readonly plans = inject(TrainingPlanService);
  private readonly route = inject(ActivatedRoute);

  protected readonly exercise = signal<ExerciseSummary | null>(null);

  // The bar names the screen the way its h1 does (mobile-native-feel).
  protected readonly screenTitleEffect = useScreenTitle(() => this.exercise()?.name ?? null);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /**
   * A 404 is its own state, and here it means one of two things: the id is wrong, or the exercise is
   * not in this member's plan. Both are answered the same way, and retrying cannot help either.
   */
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
      this.exercise.set(await this.plans.getMyExercise(this.id));
    } catch (failure) {
      // Outlet 4 — a screen that could not be populated at all. `notFound` is now a KIND rather
      // than a hand-written status test, so a 404 here reads the same way it does on every other
      // detail screen.
      if (classifyFailure(failure).kind === 'notFound') {
        this.notFound.set(true);
      } else {
        this.loadFailed.set(true);
      }
    } finally {
      this.loading.set(false);
    }
  }
}
