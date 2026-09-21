import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ExerciseSummary } from './exercise.models';
import {
  MemberPlan,
  TrainerMemberPage,
  TrainerMemberQuery,
  TrainingPlanDetail,
  TrainingPlanRequest,
} from './training-plan.models';

/**
 * Training plans (prd.md FR-015, FR-016, FR-017), across BOTH API surfaces — the trainer's
 * `/api/trainer/members` and `/api/trainer/plans`, and the member's `/api/plans`.
 *
 * One service rather than two because it is one feature read from two seats, and the SPA has no
 * layering rule that would separate them. The methods are grouped and labelled instead.
 *
 * Relative /api paths, like every other service here: the SPA is served from the API's own wwwroot,
 * so these are same-origin and the auth cookie rides along.
 *
 * Nothing here catches. A failed save has to reach the screen — a trainer who believes a plan was
 * assigned when it was not is the failure mode that matters.
 *
 * There is no `remove`. Assignment archives the plan it replaces, so the API exposes no DELETE.
 */
@Injectable({ providedIn: 'root' })
export class TrainingPlanService {
  private readonly http = inject(HttpClient);

  // --- The trainer's surface: /api/trainer/*, behind TrainerOrAdmin. ---
  //
  // Reads are addressed by MEMBER since S-22 — the plan list, the member picker and the by-id read
  // went with the /trainer/plans screen. Writes stay addressed by plan id.

  /**
   * One page of the trainer's member list (S-22): active members, searched by NAME only, each with
   * their active plan's name. Absent or empty fields are omitted rather than sent blank, for the
   * reason `MemberAdminService.getMembers` gives — the API binds `page`/`pageSize` as integers.
   */
  getTrainerMembers(query: TrainerMemberQuery = {}): Promise<TrainerMemberPage> {
    let params = new HttpParams();

    const search = query.search?.trim();
    if (search) {
      params = params.set('search', search);
    }

    if (query.page !== undefined) {
      params = params.set('page', query.page);
    }

    if (query.pageSize !== undefined) {
      params = params.set('pageSize', query.pageSize);
    }

    return firstValueFrom(this.http.get<TrainerMemberPage>('/api/trainer/members', { params }));
  }

  /**
   * A member and their active plan (S-22) — the builder's load when it is reached through the
   * member. `plan` is null for a member with none; a 404 means the MEMBER does not exist.
   */
  getMemberPlan(memberId: string): Promise<MemberPlan> {
    return firstValueFrom(
      this.http.get<MemberPlan>(`/api/trainer/members/${encodeURIComponent(memberId)}/plan`),
    );
  }

  /**
   * Assigns a new plan, ARCHIVING whatever the member was following (FR-016). Silent by design —
   * there is no confirmation step, because nothing is lost: the old plan is archived, not deleted.
   */
  create(request: TrainingPlanRequest): Promise<TrainingPlanDetail> {
    return firstValueFrom(this.http.post<TrainingPlanDetail>('/api/trainer/plans', request));
  }

  /** Edits a plan in place: its name and its ENTIRE item list. Does not move it between members. */
  update(id: string, request: TrainingPlanRequest): Promise<TrainingPlanDetail> {
    return firstValueFrom(
      this.http.put<TrainingPlanDetail>(`/api/trainer/plans/${encodeURIComponent(id)}`, request),
    );
  }

  // --- The member's surface: /api/plans, behind ActiveMember, always scoped to the caller. ---

  /**
   * The signed-in member's active plan, or `null` when they have none.
   *
   * THE API ANSWERS 204, NOT 404, FOR "NO PLAN", and this method is where that becomes a `null`.
   * `HttpClient` gives a 204 body as `null` already, so the coalesce is belt-and-braces — but the
   * distinction it preserves is the point: the screen must show an empty card for "no plan yet" and
   * an error for "the request failed", and a thrown 404 would collapse the two.
   *
   * There is no member id parameter anywhere on this surface. The server takes it from the cookie.
   */
  async getMine(): Promise<TrainingPlanDetail | null> {
    const plan = await firstValueFrom(this.http.get<TrainingPlanDetail>('/api/plans/mine'));

    return plan ?? null;
  }

  /**
   * One exercise's instructions and video (FR-020), reachable ONLY through the caller's own plan.
   *
   * 404 for an exercise that is not in the member's active plan — that is the "no standalone library
   * browsing" Non-Goal enforced rather than merely respected, so the caller must keep notFound as its
   * own state.
   */
  getMyExercise(exerciseId: string): Promise<ExerciseSummary> {
    return firstValueFrom(
      this.http.get<ExerciseSummary>(`/api/plans/mine/exercises/${encodeURIComponent(exerciseId)}`),
    );
  }
}
