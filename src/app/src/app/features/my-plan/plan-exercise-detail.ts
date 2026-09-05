import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ExerciseSummary } from '../../core/training/exercise.models';
import { TrainingPlanService } from '../../core/training/training-plan.service';
import { embedUrl, isVideoId } from '../../core/training/youtube';

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
 * THE VIDEO HANDLING IS UNCHANGED IN SUBSTANCE, and must stay that way: the id is re-checked against
 * the same 11-character pattern immediately before it is trusted. The spec beside this file proves a
 * malformed id and a `javascript:` string render no iframe — exercise-library's review required that
 * test, and copying the component without copying the test is precisely how it would be lost.
 */
@Component({
  imports: [RouterLink],
  selector: 'app-plan-exercise-detail',
  styleUrl: './plan-exercise-detail.scss',
  templateUrl: './plan-exercise-detail.html',
})
export class PlanExerciseDetail implements OnInit {
  private readonly plans = inject(TrainingPlanService);
  private readonly route = inject(ActivatedRoute);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly exercise = signal<ExerciseSummary | null>(null);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /**
   * A 404 is its own state, and here it means one of two things: the id is wrong, or the exercise is
   * not in this member's plan. Both are answered the same way, and retrying cannot help either.
   */
  protected readonly notFound = signal(false);

  private id = '';

  /**
   * The player URL, trusted once per video id.
   *
   * THE ONLY bypassSecurityTrust* CALL IN THIS COMPONENT, and the isVideoId guard is why it is safe:
   * the id is re-checked against the same 11-character pattern the server enforces immediately
   * before it is trusted. Computed rather than a template getter on purpose — a getter would
   * re-trust the value on every change detection cycle.
   */
  protected readonly playerUrl = computed<SafeResourceUrl | null>(() => {
    const videoId = this.exercise()?.videoId;

    return isVideoId(videoId)
      ? this.sanitizer.bypassSecurityTrustResourceUrl(embedUrl(videoId))
      : null;
  });

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
      if ((failure as HttpErrorResponse)?.status === 404) {
        this.notFound.set(true);
      } else {
        this.loadFailed.set(true);
      }
    } finally {
      this.loading.set(false);
    }
  }
}
