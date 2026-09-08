import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  AccessCodeView,
  Member,
  MemberDetail,
  MemberFilter,
  MemberRequest,
  PendingMember,
  TrainerSummary,
} from './member-admin.models';

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
   * The full member list, optionally narrowed to one filter position (FR-005). Since S-14 it also
   * carries the people who have no account at all. Admins are INCLUDED since
   * S-04 — the Trainer role is granted from this list, and an owner who teaches has to be reachable
   * there. The screen decides which actions a row offers; the API refuses a block on an admin
   * regardless.
   *
   * When no filter is given the parameter is OMITTED rather than sent empty: the endpoint binds it
   * as a nullable enum and refuses an unparseable value with a 400, so `?filter=` would be a broken
   * request rather than "no filter".
   */
  getMembers(filter?: MemberFilter): Promise<Member[]> {
    const options = filter ? { params: new HttpParams().set('filter', filter) } : {};

    return firstValueFrom(this.http.get<Member[]>('/api/admin/members', options));
  }

  /** One member, with the contact details the edit form needs. */
  getMember(id: string): Promise<MemberDetail> {
    return firstValueFrom(
      this.http.get<MemberDetail>(`/api/admin/members/${encodeURIComponent(id)}`),
    );
  }

  /**
   * Records a person who has no account (S-14). Creates a member and nothing else — no login, no
   * password, no invitation; they get one only if they later register with a member code.
   */
  async create(request: MemberRequest): Promise<string> {
    const created = await firstValueFrom(
      this.http.post<{ id: string }>('/api/admin/members', request),
    );

    return created.id;
  }

  async update(id: string, request: MemberRequest): Promise<void> {
    await firstValueFrom(
      this.http.put<void>(`/api/admin/members/${encodeURIComponent(id)}`, request),
    );
  }

  /**
   * The people a class occurrence may name as its instructor (prd-v2 FR-009): ACTIVE accounts
   * holding the Trainer role, by display name.
   *
   * A separate endpoint rather than filtering `getMembers()` client-side: the filter belongs on the
   * server, where it already backs the validation that refuses a non-trainer on write, and a
   * dropdown has no business loading every account's email address to build itself.
   */
  getTrainers(): Promise<TrainerSummary[]> {
    return firstValueFrom(this.http.get<TrainerSummary[]>('/api/admin/trainers'));
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

  /**
   * Grant the Trainer role (S-04). Additive — it costs the account nothing it already had, and on
   * its own it grants nothing either; S-06 consumes it to populate the instructor selection.
   */
  async grantTrainer(id: string): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(`/api/admin/members/${encodeURIComponent(id)}/roles/trainer`, null),
    );
  }

  async revokeTrainer(id: string): Promise<void> {
    await firstValueFrom(
      this.http.delete<void>(`/api/admin/members/${encodeURIComponent(id)}/roles/trainer`),
    );
  }

  /**
   * The member's outstanding code, or `null` when there is none (S-14, AM-004).
   *
   * A SEPARATE REQUEST from the list on purpose: `Member.hasAccessCode` says whether one exists, and
   * that is all a table of rows needs. Folding the code itself into the list would ship every live
   * credential the club holds to the browser every time the screen opens.
   *
   * The API answers 204 for "none", which Angular surfaces as `null` — an expired code is reported
   * that way too, since reading out a code that will be refused is worse than reading out nothing.
   */
  async getAccessCode(id: string): Promise<AccessCodeView | null> {
    const view = await firstValueFrom(
      this.http.get<AccessCodeView | null>(
        `/api/admin/members/${encodeURIComponent(id)}/access-code`,
      ),
    );

    return view ?? null;
  }

  /**
   * Issues a code, REPLACING any outstanding one — the admin pressing this again is someone who
   * mislaid the code they wrote down, and making them revoke first would be ceremony.
   */
  issueAccessCode(id: string): Promise<AccessCodeView> {
    return firstValueFrom(
      this.http.post<AccessCodeView>(
        `/api/admin/members/${encodeURIComponent(id)}/access-code`,
        null,
      ),
    );
  }

  /** Kills the outstanding code. Idempotent — revoking nothing is a no-op, not an error. */
  async revokeAccessCode(id: string): Promise<void> {
    await firstValueFrom(
      this.http.delete<void>(`/api/admin/members/${encodeURIComponent(id)}/access-code`),
    );
  }
}
