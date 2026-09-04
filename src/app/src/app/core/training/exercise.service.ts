import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ExerciseRequest, ExerciseSummary } from './exercise.models';

/**
 * The admin's exercise library (prd.md FR-018, FR-019).
 *
 * Relative /api paths, like every other service here: the SPA is served from the API's own wwwroot,
 * so these are same-origin and the auth cookie rides along.
 *
 * Nothing here catches. A failed create or activation has to reach the screen — an admin who
 * believes an exercise was saved when it was not is the failure mode that matters.
 *
 * There is no `remove`. Deactivation replaces deletion, so the API exposes no DELETE.
 */
@Injectable({ providedIn: 'root' })
export class ExerciseService {
  private readonly http = inject(HttpClient);

  /**
   * Every exercise, active and inactive, active first and then by name.
   *
   * Unfiltered on purpose: the list screen's "show inactive" toggle filters rows it already holds,
   * and the form reuses this same call to build its muscle-group and difficulty suggestions.
   */
  getAll(): Promise<ExerciseSummary[]> {
    return firstValueFrom(this.http.get<ExerciseSummary[]>('/api/admin/exercises'));
  }

  /** One exercise, for the detail screen and the edit form. */
  getById(id: string): Promise<ExerciseSummary> {
    return firstValueFrom(
      this.http.get<ExerciseSummary>(`/api/admin/exercises/${encodeURIComponent(id)}`),
    );
  }

  create(request: ExerciseRequest): Promise<ExerciseSummary> {
    return firstValueFrom(this.http.post<ExerciseSummary>('/api/admin/exercises', request));
  }

  update(id: string, request: ExerciseRequest): Promise<ExerciseSummary> {
    return firstValueFrom(
      this.http.put<ExerciseSummary>(`/api/admin/exercises/${encodeURIComponent(id)}`, request),
    );
  }

  /** Retires an exercise. Idempotent — deactivating an already-inactive one is not an error. */
  deactivate(id: string): Promise<ExerciseSummary> {
    return firstValueFrom(
      this.http.post<ExerciseSummary>(
        `/api/admin/exercises/${encodeURIComponent(id)}/deactivate`,
        {},
      ),
    );
  }

  /**
   * Puts a retired exercise back into circulation.
   *
   * CAN FAIL WITH 409 name_taken even though the request carries no name: deactivating released the
   * name, and another exercise may hold it now. The caller must handle that, not assume success.
   */
  activate(id: string): Promise<ExerciseSummary> {
    return firstValueFrom(
      this.http.post<ExerciseSummary>(
        `/api/admin/exercises/${encodeURIComponent(id)}/activate`,
        {},
      ),
    );
  }
}
