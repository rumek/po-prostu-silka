import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TrainingPlanService } from '../../core/training/training-plan.service';
import { TrainingPlanDetail } from '../../core/training/training-plan.models';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import { PlanSummary } from '../../shared/plan-summary/plan-summary';
import { Icon } from '../../shared/icons/icon';
import { Empty } from '../../shared/forms/empty/empty';
import { Loading } from '../../shared/forms/loading/loading';
import { prescribedParameters } from '../../shared/plan-parameters/plan-parameters';

/**
 * The member's own training plan (prd.md FR-017).
 *
 * THREE STATES, NOT TWO. "Loading", "the request failed" and "you have no plan yet" are separate,
 * and the last one is a plain card rather than an error: a member who has not been given a plan has
 * nothing to retry and nothing to fix. The API says so explicitly by answering 204 rather than 404 —
 * collapsing the two into one "not found" is exactly what this screen exists not to do.
 *
 * No member id anywhere: the API takes it from the cookie, so there is no id here to get wrong and
 * no way to ask for someone else's plan.
 *
 * A prescription field that is absent is OMITTED, not rendered as a dash — the same rule
 * exercise-detail follows, and for the same reason: a trainer may prescribe a bare exercise, and
 * four empty labels would make it look broken.
 */
@Component({
  imports: [Loading, Empty, PlanSummary, RouterLink, Icon],
  selector: 'app-my-plan',
  styleUrl: './my-plan.scss',
  templateUrl: './my-plan.html',
})
export class MyPlan implements OnInit {
  private readonly plans = inject(TrainingPlanService);

  /** The plan, or null. Null means "no plan" ONLY when loading and loadFailed are both false. */
  protected readonly plan = signal<TrainingPlanDetail | null>(null);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /**
   * WHY the load failed, when the transport can say (S-19).
   *
   * The screen state itself is unchanged — this is still outlet 4, and the sentence in the template
   * still names what could not be fetched. What is new is the second half: a dead network now says
   * so, where before a killed API read exactly like a server that answered and refused.
   */
  protected readonly loadMessage = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      this.plan.set(await this.plans.getMine());
    } catch (failure) {
      // The plan is cleared too: leaving a stale one under an error banner would let the member act
      // on a plan the app no longer believes it has.
      this.plan.set(null);
      this.loadMessage.set(transportMessage(classifyFailure(failure)));
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * What the card shows under the name — only the parameters this item carries. Missing a duration
   * here would render a duration-only plank (S-15) as a name and nothing else, which is why this
   * reads the shared table rather than naming the fields.
   */
  protected readonly prescribed = prescribedParameters;
}
