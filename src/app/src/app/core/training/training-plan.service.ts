import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ExerciseSummary } from './exercise.models';
import {
  AssignableMember,
  TrainingPlanDetail,
  TrainingPlanRequest,
  TrainingPlanSummary,
} from './training-plan.models';

/**
 * Training plans (prd.md FR-015, FR-016, FR-017), across BOTH API surfaces — the trainer's
 * `/api/trainer/plans` and the member's `/api/plans`.
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

  // --- The trainer's surface: /api/trainer/plans, behind TrainerOrAdmin. ---

  /** Every ACTIVE plan in the club, ordered by member name. Archived plans are not listed anywhere. */
  getAll(): Promise<TrainingPlanSummary[]> {
    return firstValueFrom(this.http.get<TrainingPlanSummary[]>('/api/trainer/plans'));
  }

  /**
   * Who a plan may be assigned to: every ACTIVE account, id and display name only.
   *
   * Its own endpoint rather than the admin member list, which carries emails and account status and
   * is Admin-only — a trainer needs a picker, not a member register.
   */
  getAssignableMembers(): Promise<AssignableMember[]> {
    return firstValueFrom(this.http.get<AssignableMember[]>('/api/trainer/plans/members'));
  }

  /** One plan with its items in order, for the builder's edit load. */
  getById(id: string): Promise<TrainingPlanDetail> {
    return firstValueFrom(
      this.http.get<TrainingPlanDetail>(`/api/trainer/plans/${encodeURIComponent(id)}`),
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
