import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MembershipPassView } from '../../../core/admin/member-admin.models';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { useScreenTitle } from '../../../core/layout/screen-title';
import { UpLink } from '../../../core/layout/up';
import { TrainingPlanService } from '../../../core/training/training-plan.service';
import { Empty } from '../../../shared/forms/empty/empty';
import { createFormState } from '../../../shared/forms/form-state';
import { createLoadFence } from '../../../shared/forms/load-fence';
import { Loading } from '../../../shared/forms/loading/loading';
import { Icon } from '../../../shared/icons/icon';
import { List } from '../../../shared/list/list';
import { Row } from '../../../shared/list/row';
import { createPassPaymentActions } from '../../../shared/passes/pass-payment-actions';
import { PassPaymentOverlay } from '../../../shared/passes/pass-payment-overlay';
import { PassPaymentStatus } from '../../../shared/passes/pass-payment-status';

/**
 * A member's karnets as a TRAINER sees them (pass-paid-flag): read-only, with the one thing a trainer
 * may do to a karnet — record that it was paid, or undo that.
 *
 * <h2>Why a screen of its own</h2>
 *
 * "Nieopłacony" means ANY unpaid karnet, and a month that expired unpaid is not the karnet covering
 * today — so a row action on the member list could not settle it. This screen lists every karnet,
 * like the admin's, minus issue, edit and revoke, which stay the admin's alone (S-16, MP-04..06).
 *
 * Mounted beside the plan builder (`/trainer/members/:id/passes`, a child of the trainer's list), gated
 * by `trainerGuard` to match the API's TrainerOrAdmin — the S-25 rule that the menu, the guard and the
 * policy agree. It has no menu entry of its own; it is reached from a member row.
 *
 * The payment actions behave exactly as on the admin's screen and share its overlay and status.
 */
@Component({
  imports: [
    DatePipe,
    Empty,
    Icon,
    List,
    Loading,
    PassPaymentOverlay,
    PassPaymentStatus,
    Row,
    UpLink,
  ],
  selector: 'app-trainer-member-passes',
  styleUrl: './trainer-member-passes.scss',
  templateUrl: './trainer-member-passes.html',
})
export class TrainerMemberPasses implements OnInit {
  private readonly plans = inject(TrainingPlanService);
  private readonly route = inject(ActivatedRoute);

  protected readonly memberId = signal('');
  protected readonly displayName = signal<string | null>(null);
  protected readonly passes = signal<MembershipPassView[]>([]);

  protected readonly state = createFormState();
  private readonly fence = createLoadFence();

  /** A 404 — unknown member, or staff. A screen state, never a toast: there is nothing behind it. */
  protected readonly notFound = signal<string | null>(null);

  // The bar names the screen the way its h1 does (mobile-native-feel).
  protected readonly screenTitleEffect = useScreenTitle(() =>
    this.displayName() ? `Karnety — ${this.displayName()}` : null,
  );

  /** Where "up" goes — the route's screen parent, the trainer's member list. */
  protected readonly membersLink: string =
    (this.route.snapshot.data['parent'] as string | undefined) ?? '/';

  /**
   * The payment overlay and "Cofnij płatność", shared with the admin's karnet screen. The returned
   * row replaces the held one: patching IS safe here, unlike after an issue or an edit, because payment
   * touches no entry and the view's entry counts were read in the same request.
   */
  protected readonly payment = createPassPaymentActions((view) =>
    this.passes.update((rows) => rows.map((row) => (row.id === view.id ? view : row))),
  );

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (id === null) {
      this.state.loadFailed.set(true);
      this.state.loading.set(false);
      return;
    }

    this.memberId.set(id);
    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();

    this.state.loading.set(true);
    this.state.loadFailed.set(false);
    this.notFound.set(null);

    try {
      const body = await this.plans.getMemberPasses(this.memberId());

      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.displayName.set(body.displayName);
      this.passes.set(body.passes);
    } catch (failure) {
      if (!this.fence.isCurrent(generation)) {
        return;
      }

      const info = classifyFailure(failure);
      if (info.kind === 'notFound') {
        this.notFound.set(transportMessage(info));
      } else {
        this.state.loadFailed.set(true);
      }
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.state.loading.set(false);
      }
    }
  }
}
