import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ClassGroupRequest, ClassGroupSummary } from './class-group.models';

/**
 * The admin's class-group definitions (prd-v2 FR-004, FR-005, FR-006, FR-007).
 *
 * Relative /api paths, like every other service here: the SPA is served from the API's own wwwroot,
 * so these are same-origin and the auth cookie rides along.
 *
 * Nothing here catches. A failed create or activation has to reach the screen — an admin who
 * believes a group was saved when it was not is the failure mode that matters.
 *
 * There is no `remove`. FR-006 replaces deletion with deactivation, so the API exposes no DELETE.
 */
@Injectable({ providedIn: 'root' })
export class ClassGroupService {
  private readonly http = inject(HttpClient);

  /**
   * Every group, active and inactive, active first and then by name.
   *
   * Unfiltered on purpose: the list screen's "show inactive" toggle filters rows it already holds,
   * so flicking it costs no round trip.
   */
  getAll(): Promise<ClassGroupSummary[]> {
    return firstValueFrom(this.http.get<ClassGroupSummary[]>('/api/admin/class-groups'));
  }

  /** One group, for the edit form — see class.service.getById for why this is its own endpoint. */
  getById(id: string): Promise<ClassGroupSummary> {
    return firstValueFrom(
      this.http.get<ClassGroupSummary>(`/api/admin/class-groups/${encodeURIComponent(id)}`),
    );
  }

  create(request: ClassGroupRequest): Promise<ClassGroupSummary> {
    return firstValueFrom(this.http.post<ClassGroupSummary>('/api/admin/class-groups', request));
  }

  update(id: string, request: ClassGroupRequest): Promise<ClassGroupSummary> {
    return firstValueFrom(
      this.http.put<ClassGroupSummary>(
        `/api/admin/class-groups/${encodeURIComponent(id)}`,
        request,
      ),
    );
  }

  /** Retires a group. Idempotent — deactivating an already-inactive group is not an error. */
  deactivate(id: string): Promise<ClassGroupSummary> {
    return firstValueFrom(
      this.http.post<ClassGroupSummary>(
        `/api/admin/class-groups/${encodeURIComponent(id)}/deactivate`,
        {},
      ),
    );
  }

  /**
   * Puts a retired group back into circulation.
   *
   * CAN FAIL WITH 409 name_taken even though the request carries no name: deactivating released the
   * name, and another group may hold it now. The caller must handle that, not assume success.
   */
  activate(id: string): Promise<ClassGroupSummary> {
    return firstValueFrom(
      this.http.post<ClassGroupSummary>(
        `/api/admin/class-groups/${encodeURIComponent(id)}/activate`,
        {},
      ),
    );
  }
}
