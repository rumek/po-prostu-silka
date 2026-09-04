import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ExerciseService } from '../../../core/training/exercise.service';
import { ExerciseSummary } from '../../../core/training/exercise.models';
import { embedUrl, isVideoId } from '../../../core/training/youtube';

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
  imports: [RouterLink],
  selector: 'app-exercise-detail',
  styleUrl: './exercise-detail.scss',
  templateUrl: './exercise-detail.html',
})
export class ExerciseDetail implements OnInit {
  private readonly exercises = inject(ExerciseService);
  private readonly route = inject(ActivatedRoute);
  private readonly sanitizer = inject(DomSanitizer);

  protected readonly exercise = signal<ExerciseSummary | null>(null);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /** A 404 is its own state: the id is wrong, so retrying the same request cannot help. */
  protected readonly notFound = signal(false);

  private id = '';

  /**
   * The player URL, trusted once per video id.
   *
   * THIS IS THE ONLY bypassSecurityTrust* CALL IN THE APP, and the isVideoId guard is why it is safe:
   * the id is re-checked against the same 11-character pattern the server enforces immediately
   * before it is trusted. Computed rather than written as a template getter on purpose — a getter
   * would re-trust the value on every change detection cycle.
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
      this.exercise.set(await this.exercises.getById(this.id));
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

  /** The edit screen for this exercise — the whole point of a read-only view having a way out. */
  protected editLink(): unknown[] {
    return ['/admin/exercises', this.id, 'edit'];
  }
}
