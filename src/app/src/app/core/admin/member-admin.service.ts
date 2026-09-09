import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  AccessCodeView,
  IssuePassRequest,
  Member,
  MemberDetail,
  MemberFilter,
  MemberRequest,
  MembershipPassView,
  TrainerSummary,
} from './member-admin.models';

/**
 * The admin's member surface: the full member list and block/unblock (S-02), the accountless records
 * (S-14) and the karnet (S-16).
 *
 * The pending queue and approve are GONE (S-16, MP-03) — the API removed both routes, so re-adding
 * methods for them would 404.
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

  /**
   * The full member list, optionally narrowed to one filter position (FR-005). Since S-14 it also
   * carries the people who have no account at all. The `Pending` position is retired with the
   * approval flow — nothing produces such an account, so the filter would always come back empty.
   * Admins are INCLUDED since
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

  // --- karnety (S-16) ----------------------------------------------------------------------------

  /**
   * The member's whole karnet history, newest first — expired passes included.
   *
   * HISTORY, NOT "THE CURRENT PASS". The admin's question at the desk is usually what this person has
   * bought and used, which one current row cannot answer; the row covering today carries
   * `coversToday` so the screen can highlight it without a second request.
   */
  getPasses(memberId: string): Promise<MembershipPassView[]> {
    return firstValueFrom(
      this.http.get<MembershipPassView[]>(
        `/api/admin/members/${encodeURIComponent(memberId)}/passes`,
      ),
    );
  }

  /** Issues a karnet. Refused when it overlaps one the member already holds — passes are a history. */
  issuePass(memberId: string, request: IssuePassRequest): Promise<MembershipPassView> {
    return firstValueFrom(
      this.http.post<MembershipPassView>(
        `/api/admin/members/${encodeURIComponent(memberId)}/passes`,
        request,
      ),
    );
  }

  /**
   * Corrects a karnet. Editing NEVER reattributes history: bookings point at a pass by id, so
   * narrowing a range does not hand entries back, and the entry count cannot go below what is spent.
   */
  updatePass(
    memberId: string,
    passId: string,
    request: IssuePassRequest,
  ): Promise<MembershipPassView> {
    return firstValueFrom(
      this.http.put<MembershipPassView>(
        `/api/admin/members/${encodeURIComponent(memberId)}/passes/${encodeURIComponent(passId)}`,
        request,
      ),
    );
  }

  /** Removes a karnet. Refused while any booking still points at it (`has_active_bookings`). */
  async revokePass(memberId: string, passId: string): Promise<void> {
    await firstValueFrom(
      this.http.delete<void>(
        `/api/admin/members/${encodeURIComponent(memberId)}/passes/${encodeURIComponent(passId)}`,
      ),
    );
  }

  /**
   * The CALLER's own karnet covering today, or `null` when they hold none (MP-07).
   *
   * A different route from the four above and deliberately so: it takes no member id at all, so
   * there is nothing to tamper with, and it answers to the ActiveMember policy rather than Admin.
   * The API says 204 for "none", which Angular surfaces as `null` — and an EXPIRED pass is reported
   * that way too, because the member's question is "can I train", not "what did I once hold".
   */
  async getMyPass(): Promise<MembershipPassView | null> {
    const view = await firstValueFrom(this.http.get<MembershipPassView | null>('/api/passes/mine'));

    return view ?? null;
  }
}
