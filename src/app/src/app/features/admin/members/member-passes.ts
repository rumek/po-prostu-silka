import { DatePipe } from '@angular/common';
import { Component, ElementRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { useScreenTitle } from '../../../core/layout/screen-title';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import {
  IssuePassRequest,
  MemberDetail,
  MembershipPassView,
} from '../../../core/admin/member-admin.models';
import { membershipPassFailureMessage } from '../../../core/admin/membership-pass-failure';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { createFormState } from '../../../shared/forms/form-state';
import { ToastService } from '../../../shared/toast/toast.service';
import { createLoadFence } from '../../../shared/forms/load-fence';
import { Field } from '../../../shared/forms/field/field';
import { Loading } from '../../../shared/forms/loading/loading';
import { Empty } from '../../../shared/forms/empty/empty';
import { Row } from '../../../shared/list/row';
import { List } from '../../../shared/list/list';
import { Icon } from '../../../shared/icons/icon';
import { Checkbox } from '../../../shared/forms/checkbox/checkbox';
import { PassPaymentOverlay } from '../../../shared/passes/pass-payment-overlay';
import { PassPaymentStatus } from '../../../shared/passes/pass-payment-status';
import { createPassPaymentActions } from '../../../shared/passes/pass-payment-actions';
import { clubToday } from '../../../core/passes/club-today';

/**
 * Bounds mirrored from MembershipPassRules (src/Application/Members/MembershipPassRules.cs).
 *
 * MIRRORED, NOT AUTHORITATIVE. The server refuses the same values with its own message; these exist
 * so the admin is told before the round trip rather than after it. Keep the two in step — a form that
 * accepts what the API rejects is a worse experience than one with no validation at all.
 */
const TYPE_NAME_MAX_LENGTH = 100;
const MIN_ENTRY_COUNT = 1;
const MAX_ENTRY_COUNT = 500;
const MAX_VALIDITY_DAYS = 400;

/**
 * One member's karnety: the history, and the form that issues the next one (S-16, MP-04..MP-06).
 *
 * <p>
 * A SCREEN OF ITS OWN rather than a section on the member form. The two answer different questions —
 * who this person is, versus what they are entitled to — and the karnet is the thing the club touches
 * repeatedly while the contact details are written once. Folding it in would also mean one submit
 * button for two unrelated writes.
 * </p>
 *
 * <p>
 * ENTRIES LEFT IS A READING, NOT A FACT. The API derives it from active bookings, so anything that
 * books or releases a spot changes it — which is why every action here refetches the whole history
 * instead of patching the row it just wrote. See MembershipPass in the Domain for why there is no
 * counter to patch in the first place.
 * </p>
 *
 * <p>
 * The `generation` fencing counter is the same guard the other admin screens carry: two responses to
 * the same list can land in either order, and the loser must not write back.
 * </p>
 */
@Component({
  imports: [
    List,
    Row,
    Icon,
    Empty,
    Loading,
    Field,
    Checkbox,
    PassPaymentOverlay,
    PassPaymentStatus,
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
  ],
  selector: 'app-member-passes',
  styleUrl: './member-passes.scss',
  templateUrl: './member-passes.html',
})
export class MemberPasses implements OnInit {
  private readonly members = inject(MemberAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly memberId = signal<string>('');
  protected readonly member = signal<MemberDetail | null>(null);

  // The bar names the screen the way its h1 does (mobile-native-feel).
  protected readonly screenTitleEffect = useScreenTitle(() =>
    this.member() ? `Karnety — ${this.member()!.displayName}` : null,
  );

  protected readonly passes = signal<MembershipPassView[]>([]);
  protected readonly state = createFormState();
  private readonly fence = createLoadFence();

  /** The pass being edited, or null while the form is issuing a new one. */
  protected readonly editingId = signal<string | null>(null);

  /**
   * Revoking is a ROW action, so it reports through the toast (outlet 3) while the issue form keeps
   * its banner (outlet 2). Same screen, two outlets, and the rule is what decides which: the form
   * banner sits above the controls the admin would correct, and a revoked row has no such controls.
   */
  private readonly toast = inject(ToastService);

  /** The row whose revoke is in flight, so only that row's button shows a busy state. */
  protected readonly revokingId = signal<string | null>(null);

  protected readonly editing = computed(() => this.editingId() !== null);

  /**
   * The pass covering today, which the header states in one line — the admin's first question at the
   * desk is almost always "is this person's karnet live", and making them read the table for it would
   * be a worse answer than a sentence.
   */
  protected readonly current = computed(() => this.passes().find((p) => p.coversToday) ?? null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    typeName: ['', [Validators.required, Validators.maxLength(TYPE_NAME_MAX_LENGTH)]],
    validFrom: ['', [Validators.required]],
    validTo: ['', [Validators.required]],
    entryCount: [
      10,
      [Validators.required, Validators.min(MIN_ENTRY_COUNT), Validators.max(MAX_ENTRY_COUNT)],
    ],

    // pass-paid-flag. ISSUE ONLY — hidden while editing, and never sent on an edit: payment on an
    // existing karnet changes through the row actions below. Unchecked by default, as the club's
    // spreadsheet leaves the cell empty. `app-checkbox` has no ControlValueAccessor, so the box is
    // bound to `paid` by hand (`togglePaid`).
    paid: [false],
    paidAt: [''],
  });

  /**
   * The payment overlay and "Cofnij płatność", shared with the trainer's karnet screen. The returned
   * row replaces the held one: patching IS safe here, unlike after an issue or an edit, because payment
   * touches no entry and the view's entry counts were read in the same request.
   */
  protected readonly payment = createPassPaymentActions((view) =>
    this.passes.update((rows) => rows.map((row) => (row.id === view.id ? view : row))),
  );

  protected readonly today = clubToday();

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (id === null) {
      this.state.loadFailed.set(true);
      this.state.loading.set(false);
      return;
    }

    this.memberId.set(id);

    // The member's name, so the screen says WHOSE karnety these are. Failure here is not fatal —
    // the history is still readable and useful — so it is deliberately not folded into loadFailed.
    try {
      this.member.set(await this.members.getMember(id));
    } catch {
      this.member.set(null);
    }

    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();

    this.state.loading.set(true);
    this.state.loadFailed.set(false);

    try {
      const rows = await this.members.getPasses(this.memberId());

      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.passes.set(rows);
    } catch {
      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.passes.set([]);
      this.state.loadFailed.set(true);
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.state.loading.set(false);
      }
    }
  }

  /**
   * Loads a pass into the form for correction.
   *
   * The date inputs take `YYYY-MM-DD`, which is exactly the shape the API already sends — so the
   * values go across UNPARSED. Round-tripping them through `Date` would reintroduce the timezone
   * shift `DateOnly` exists to avoid, and could move a karnet's last day by one.
   */
  protected edit(pass: MembershipPassView): void {
    this.editingId.set(pass.id);
    this.state.error.set(null);

    this.form.setValue({
      typeName: pass.typeName,
      validFrom: pass.validFrom,
      validTo: pass.validTo,
      entryCount: pass.entryCount,
      paid: false,
      paidAt: '',
    });

    // The form sits ABOVE the history, so on a phone the row the admin tapped and the form it filled
    // are screens apart. Optional calls: jsdom has neither scrollIntoView nor matchMedia.
    const still = globalThis.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? true;
    this.host.nativeElement
      .querySelector('#passes-form-title')
      ?.scrollIntoView?.({ behavior: still ? 'auto' : 'smooth', block: 'start' });
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
    this.state.error.set(null);
    this.form.reset({
      typeName: '',
      validFrom: '',
      validTo: '',
      entryCount: 10,
      paid: false,
      paidAt: '',
    });
  }

  /** Ticking "Opłacony" fills the day with today, so the common case is one tap. */
  protected togglePaid(): void {
    const paid = !this.form.controls.paid.value;

    this.form.patchValue({ paid, paidAt: paid ? this.today : '' });
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    // Checked before the round trip so the admin sees the whole rule rather than the server's
    // one-field answer. The API enforces it too — this is a courtesy, never the boundary.
    const { validFrom, validTo } = this.form.getRawValue();
    if (validTo < validFrom) {
      this.state.error.set(membershipPassFailureMessage('invalid_range'));
      return;
    }

    // The span, which the API answers with the SAME invalid_range reason. Without this check the
    // admin is told the end date precedes the start when it plainly does not - the one case where
    // the shared message is wrong about what went wrong.
    const spanDays =
      Math.round(
        (new Date(validTo).getTime() - new Date(validFrom).getTime()) / (24 * 60 * 60 * 1000),
      ) + 1;
    if (spanDays > MAX_VALIDITY_DAYS) {
      this.state.error.set(`Karnet nie może obejmować więcej niż ${MAX_VALIDITY_DAYS} dni.`);
      return;
    }

    const { typeName, entryCount, paid, paidAt } = this.form.getRawValue();
    const passId = this.editingId();

    // The same bound the server enforces (invalid_paid_at): a payment is never dated in the future.
    // The 400-day floor stays the server's alone — nobody slips that far by accident in a date picker.
    if (passId === null && paid && (!paidAt || paidAt > this.today)) {
      this.state.error.set(membershipPassFailureMessage('invalid_paid_at'));
      return;
    }

    this.state.submitting.set(true);
    this.state.error.set(null);

    // Built field by field rather than from getRawValue(): `paid` is a form-only control, and an EDIT
    // must not carry `paidAt` at all — editing never changes payment.
    const request: IssuePassRequest = { typeName, validFrom, validTo, entryCount };
    if (passId === null) {
      request.paidAt = paid ? paidAt : null;
    }

    try {
      if (passId === null) {
        await this.members.issuePass(this.memberId(), request);
      } else {
        await this.members.updatePass(this.memberId(), passId, request);
      }

      this.cancelEdit();

      // Refetched rather than patched: entries used is derived server-side, and the row this write
      // returned is already stale the moment anybody books.
      await this.load();
    } catch (failure) {
      this.state.error.set(messageFor(failure));
    } finally {
      this.state.submitting.set(false);
    }
  }

  protected async revoke(pass: MembershipPassView): Promise<void> {
    this.revokingId.set(pass.id);
    this.state.error.set(null);

    try {
      await this.members.revokePass(this.memberId(), pass.id);

      // Clear the form if it was editing the pass that just went away, so the admin is not left
      // looking at a form that would recreate it.
      if (this.editingId() === pass.id) {
        this.cancelEdit();
      }

      this.toast.success('Karnet został usunięty.');
      await this.load();
    } catch (failure) {
      this.toast.error(messageFor(failure));
    } finally {
      this.revokingId.set(null);
    }
  }
}

/**
 * The words for a refused karnet write.
 *
 * `transportMessage` first, because a 429, a 500 or a dead network is not a karnet rule — it used to
 * read as "nie udało się zapisać karnetu", which invites the admin to correct dates that were never
 * the problem.
 */
function messageFor(failure: unknown): string {
  const info = classifyFailure(failure);

  return transportMessage(info) ?? membershipPassFailureMessage(info.reason);
}
