import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PendingMember } from './member-admin.models';

/**
 * The admin's member surface. Two calls, matching what the API ships (S-01 D5): the pending queue
 * and approve. No reject, no block, no full member list — S-02 owns those.
 *
 * Relative /api paths, like AuthService: the SPA is served from the API's own wwwroot, so these are
 * same-origin and the auth cookie rides along.
 *
 * Nothing here catches. A failed approve has to reach the screen, which leaves the row in place and
 * says so — an admin who believes someone was approved when they were not is the failure mode that
 * matters here.
 */
@Injectable({ providedIn: 'root' })
export class MemberAdminService {
  private readonly http = inject(HttpClient);

  getPending(): Promise<PendingMember[]> {
    return firstValueFrom(this.http.get<PendingMember[]>('/api/admin/members/pending'));
  }

  async approve(id: string): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(`/api/admin/members/${encodeURIComponent(id)}/approve`, null),
    );
  }
}
