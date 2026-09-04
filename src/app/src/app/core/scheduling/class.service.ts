import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ClassRequest, DuplicateResult, ScheduledClass } from './class.models';

/**
 * A [from, to) window as the two read endpoints expect it: ISO-8601 UTC instants, or nothing at all.
 *
 * The screens work in the BROWSER's local clock — "this week" starts at local Monday midnight — and
 * `toISOString()` is where that becomes an instant. Sending a local wall-clock string instead would
 * shift every window by the UTC offset, which is silent: the week would simply contain the wrong
 * classes at its edges.
 */
function rangeParams(from?: Date, to?: Date): HttpParams | undefined {
  if (!from || !to) {
    return undefined;
  }

  return new HttpParams().set('from', from.toISOString()).set('to', to.toISOString());
}

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

  /**
   * The member's schedule, flat and time-ordered.
   *
   * Both bounds or neither — the API refuses a half-supplied range with `invalid_range` rather than
   * pairing one bound with a default nobody asked for. Omitted, the server answers the fortnight it
   * answered before the calendar existed.
   */
  getSchedule(from?: Date, to?: Date): Promise<ScheduledClass[]> {
    return firstValueFrom(
      this.http.get<ScheduledClass[]>('/api/classes', { params: rangeParams(from, to) }),
    );
  }

  /** The admin list. Omit the range for the unbounded "everything upcoming" it has always returned. */
  getAdminClasses(from?: Date, to?: Date): Promise<ScheduledClass[]> {
    return firstValueFrom(
      this.http.get<ScheduledClass[]>('/api/admin/classes', { params: rangeParams(from, to) }),
    );
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

  /**
   * Cancels a class (FR-013, S-09). NOT a delete: the class stays on the admin's calendar as
   * `Cancelled`, its booking rows stay `Active`, and every member holding one is emailed and pushed
   * in the same transaction that flipped the status.
   *
   * Returns the updated class so the caller can replace its own row rather than refetch — the only
   * field that moved is `status`, and the tile has to stop offering to cancel it again.
   */
  cancel(id: string): Promise<ScheduledClass> {
    return firstValueFrom(
      this.http.post<ScheduledClass>(`/api/admin/classes/${encodeURIComponent(id)}/cancel`, {}),
    );
  }

  duplicate(id: string, weeks: number): Promise<DuplicateResult> {
    return firstValueFrom(
      this.http.post<DuplicateResult>(`/api/admin/classes/${encodeURIComponent(id)}/duplicate`, {
        weeks,
      }),
    );
  }
}
