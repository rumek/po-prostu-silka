import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Member, MemberStatus, PendingMember } from './member-admin.models';

/**
 * The admin's member surface: the pending queue and approve (S-01), plus the full member list and
 * block/unblock (S-02). Still no reject — FR-003 dropped it from the MVP.
 *
 * Relative /api paths, like AuthService: the SPA is served from the API's own wwwroot, so these are
 * same-origin and the auth cookie rides along.
 *
 * Nothing here catches. A failed mutation has to reach the screen, which leaves the row as it was
 * and says so — an admin who believes someone was approved or blocked when they were not is the
 * failure mode that matters here.
 */
@Injectable({ providedIn: 'root' })
export class MemberAdminService {
  private readonly http = inject(HttpClient);

  getPending(): Promise<PendingMember[]> {
    return firstValueFrom(this.http.get<PendingMember[]>('/api/admin/members/pending'));
  }

  /**
   * The full member list, optionally narrowed to one status (FR-005). Admins are excluded by the
   * API, not here.
   *
   * When no status is given the parameter is OMITTED rather than sent empty: the endpoint binds it
   * as a nullable enum and refuses an unparseable value with a 400, so `?status=` would be a broken
   * request rather than "no filter".
   */
  getMembers(status?: MemberStatus): Promise<Member[]> {
    const options = status ? { params: new HttpParams().set('status', status) } : {};

    return firstValueFrom(this.http.get<Member[]>('/api/admin/members', options));
  }

  async approve(id: string): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(`/api/admin/members/${encodeURIComponent(id)}/approve`, null),
    );
  }

  async block(id: string): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(`/api/admin/members/${encodeURIComponent(id)}/block`, null),
    );
  }

  async unblock(id: string): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(`/api/admin/members/${encodeURIComponent(id)}/unblock`, null),
    );
  }
}
