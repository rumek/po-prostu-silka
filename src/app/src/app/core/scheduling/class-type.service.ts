import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ClassTypeRequest, ClassTypeSummary } from './class-type.models';

/**
 * The admin's class-type definitions (prd-v2 FR-004, FR-005, FR-006, FR-007).
 *
 * Relative /api paths, like every other service here: the SPA is served from the API's own wwwroot,
 * so these are same-origin and the auth cookie rides along.
 *
 * Nothing here catches. A failed create or activation has to reach the screen — an admin who
 * believes a type was saved when it was not is the failure mode that matters.
 *
 * There is no `remove`. FR-006 replaces deletion with deactivation, so the API exposes no DELETE.
 */
@Injectable({ providedIn: 'root' })
export class ClassTypeService {
  private readonly http = inject(HttpClient);

  /**
   * Every type, active and inactive, active first and then by name.
   *
   * Unfiltered on purpose: the list screen's "show inactive" toggle filters rows it already holds,
   * so flicking it costs no round trip.
   */
  getAll(): Promise<ClassTypeSummary[]> {
    return firstValueFrom(this.http.get<ClassTypeSummary[]>('/api/admin/class-types'));
  }

  /** One type, for the edit form — see class.service.getById for why this is its own endpoint. */
  getById(id: string): Promise<ClassTypeSummary> {
    return firstValueFrom(
      this.http.get<ClassTypeSummary>(`/api/admin/class-types/${encodeURIComponent(id)}`),
    );
  }

  create(request: ClassTypeRequest): Promise<ClassTypeSummary> {
    return firstValueFrom(this.http.post<ClassTypeSummary>('/api/admin/class-types', request));
  }

  update(id: string, request: ClassTypeRequest): Promise<ClassTypeSummary> {
    return firstValueFrom(
      this.http.put<ClassTypeSummary>(`/api/admin/class-types/${encodeURIComponent(id)}`, request),
    );
  }

  /** Retires a type. Idempotent — deactivating an already-inactive type is not an error. */
  deactivate(id: string): Promise<ClassTypeSummary> {
    return firstValueFrom(
      this.http.post<ClassTypeSummary>(
        `/api/admin/class-types/${encodeURIComponent(id)}/deactivate`,
        {},
      ),
    );
  }

  /**
   * Puts a retired type back into circulation.
   *
   * CAN FAIL WITH 409 name_taken even though the request carries no name: deactivating released the
   * name, and another type may hold it now. The caller must handle that, not assume success.
   */
  activate(id: string): Promise<ClassTypeSummary> {
    return firstValueFrom(
      this.http.post<ClassTypeSummary>(
        `/api/admin/class-types/${encodeURIComponent(id)}/activate`,
        {},
      ),
    );
  }
}
