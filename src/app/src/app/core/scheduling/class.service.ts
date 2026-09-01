import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ClassRequest, DuplicateResult, ScheduledClass } from './class.models';

/**
 * The class schedule (FR-007) and the admin's management of it (FR-011, FR-012).
 *
 * Relative /api paths, like every other service here: the SPA is served from the API's own wwwroot,
 * so these are same-origin and the auth cookie rides along.
 *
 * Nothing here catches. A failed create or duplicate has to reach the screen, which keeps the form
 * open and says what went wrong — an admin who believes a class was created when it was not is the
 * failure mode that matters.
 */
@Injectable({ providedIn: 'root' })
export class ClassService {
  private readonly http = inject(HttpClient);

  /** The member's schedule: the next fortnight, flat and time-ordered. The window is server-fixed. */
  getSchedule(): Promise<ScheduledClass[]> {
    return firstValueFrom(this.http.get<ScheduledClass[]>('/api/classes'));
  }

  /** Everything upcoming, for the admin list. Not window-bounded. */
  getAdminClasses(): Promise<ScheduledClass[]> {
    return firstValueFrom(this.http.get<ScheduledClass[]>('/api/admin/classes'));
  }

  /**
   * One class, for the edit form.
   *
   * A dedicated endpoint rather than filtering the admin list client-side: opening
   * /admin/classes/:id directly — a bookmark, a refresh, a shared link — has no list loaded to
   * filter, and that list is unbounded, so it would grow with every class the club ever schedules.
   */
  getById(id: string): Promise<ScheduledClass> {
    return firstValueFrom(
      this.http.get<ScheduledClass>(`/api/admin/classes/${encodeURIComponent(id)}`),
    );
  }

  create(request: ClassRequest): Promise<ScheduledClass> {
    return firstValueFrom(this.http.post<ScheduledClass>('/api/admin/classes', request));
  }

  update(id: string, request: ClassRequest): Promise<ScheduledClass> {
    return firstValueFrom(
      this.http.put<ScheduledClass>(`/api/admin/classes/${encodeURIComponent(id)}`, request),
    );
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`/api/admin/classes/${encodeURIComponent(id)}`));
  }

  duplicate(id: string, weeks: number): Promise<DuplicateResult> {
    return firstValueFrom(
      this.http.post<DuplicateResult>(`/api/admin/classes/${encodeURIComponent(id)}/duplicate`, {
        weeks,
      }),
    );
  }
}
