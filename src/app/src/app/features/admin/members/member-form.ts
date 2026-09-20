import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import { memberFailureMessage } from '../../../core/admin/member-failure';
import { MemberFailure, MemberRequest } from '../../../core/admin/member-admin.models';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { createFormState } from '../../../shared/forms/form-state';
import { PHONE_PATTERN, POSTAL_CODE_PATTERN } from '../../../core/auth/validation';
import { Field } from '../../../shared/forms/field/field';

/**
 * The admin's create/edit form for a member record (S-14, AM-001 and AM-002).
 *
 * <p>
 * ONE COMPONENT FOR BOTH, on the `/admin/members/new` and `/admin/members/:id` routes, because the
 * two differ in exactly two places — where the initial values come from, and which service call the
 * submit makes. Splitting them would duplicate the whole contact block and the failure mapping,
 * which is where the drift would then happen.
 * </p>
 *
 * <p>
 * CONTACT DETAILS ARE ALL-OR-NOTHING, matching the API. They are optional as a BLOCK — a person
 * recorded at the desk may have given nothing but a name, and demanding a full postal address before
 * the club may write them down would defeat the point — but a half-filled address is refused. The
 * form expresses that by requiring the five fields only once any of them is non-empty, so the admin
 * sees the rule as they type rather than as a server error.
 * </p>
 *
 * <p>
 * The DISPLAY NAME is the club's, and this is the only screen that may change it: the member's own
 * profile deliberately renders it as text (S-13, FR-006).
 * </p>
 */
@Component({
  imports: [Field, ReactiveFormsModule, RouterLink],
  selector: 'app-member-form',
  styleUrl: './member-form.scss',
  templateUrl: './member-form.html',
})
export class MemberForm implements OnInit {
  private readonly members = inject(MemberAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  /** Null while creating; the member's id while editing. */
  protected readonly memberId = signal<string | null>(null);

  protected readonly editing = computed(() => this.memberId() !== null);

  /** True once the record being edited turns out to have a login behind it. */
  protected readonly hasAccount = signal(false);

  protected readonly state = createFormState();

  /** The table, exposed so a field says the same thing whoever caught the rule. */
  protected readonly failureMessage = memberFailureMessage;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    phoneNumber: ['', [Validators.pattern(PHONE_PATTERN)]],
    street: [''],
    houseNumber: [''],
    postalCode: ['', [Validators.pattern(POSTAL_CODE_PATTERN)]],
    city: [''],
  });

  /**
   * The five contact controls, in the order the server validates them — so the message the admin
   * gets back lands on the field they are already looking at.
   */
  private static readonly CONTACT_FIELDS = [
    'phoneNumber',
    'street',
    'houseNumber',
    'postalCode',
    'city',
  ] as const;

  /**
   * Whether the admin has started an address. Drives the "finish it or clear it" hint and the
   * all-or-nothing check on submit.
   */
  protected readonly contactStarted = computed(() => this.anyContactFilled());

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (id === null) {
      return;
    }

    this.memberId.set(id);
    this.state.loading.set(true);

    try {
      const member = await this.members.getMember(id);

      this.hasAccount.set(member.userId !== null);
      this.form.patchValue({
        displayName: member.displayName,
        phoneNumber: member.phoneNumber ?? '',
        street: member.street ?? '',
        houseNumber: member.houseNumber ?? '',
        postalCode: member.postalCode ?? '',
        city: member.city ?? '',
      });
    } catch {
      this.state.loadFailed.set(true);
    } finally {
      this.state.loading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    // The all-or-nothing rule, checked before the round trip. The server enforces it too and answers
    // with whichever field it reaches first; catching it here names the whole rule instead.
    if (this.anyContactFilled() && !this.allContactFilled()) {
      this.form.markAllAsTouched();
      this.state.error.set('Uzupełnij wszystkie dane adresowe albo zostaw je puste.');
      return;
    }

    this.state.error.set(null);
    this.state.submitting.set(true);

    try {
      const request = this.toRequest();
      const id = this.memberId();

      if (id === null) {
        await this.members.create(request);
      } else {
        await this.members.update(id, request);
      }

      await this.router.navigate(['/admin/members']);
    } catch (failure) {
      this.applyFailure(failure);
    } finally {
      this.state.submitting.set(false);
    }
  }

  /** Empty strings cross the wire as nulls: "not given" is the record's state, not "". */
  private toRequest(): MemberRequest {
    const value = this.form.getRawValue();
    const trimmed = (input: string): string | null => (input.trim() === '' ? null : input.trim());

    return {
      displayName: value.displayName.trim(),
      phoneNumber: trimmed(value.phoneNumber),
      street: trimmed(value.street),
      houseNumber: trimmed(value.houseNumber),
      postalCode: trimmed(value.postalCode),
      city: trimmed(value.city),
    };
  }

  private anyContactFilled(): boolean {
    return MemberForm.CONTACT_FIELDS.some(
      (name) => (this.form.controls[name].value ?? '').trim() !== '',
    );
  }

  private allContactFilled(): boolean {
    return MemberForm.CONTACT_FIELDS.every(
      (name) => (this.form.controls[name].value ?? '').trim() !== '',
    );
  }

  /**
   * Routes a refusal to the control that caused it — outlet 1 of the rule in AGENTS.md.
   *
   * THIS SCREEN USED TO BE THE ODD ONE OUT: it banner-ed all seven reasons, including the five
   * contact codes that name a specific box, where `/profile` already put the same five codes on the
   * same five controls through the same server helper. Now both do the same thing with the same
   * sentences, and `invalid_display_name` and `conflict` stay in the banner because neither of them
   * is a contact field — `conflict` is not a field at all.
   */
  private applyFailure(failure: unknown): void {
    const info = classifyFailure(failure);

    // A 429, a 500 or a dead network belongs to no field.
    const transport = transportMessage(info);
    if (transport !== null) {
      this.state.error.set(transport);
      return;
    }

    const reason = info.reason as MemberFailure['reason'] | undefined;

    if (reason === 'invalid_display_name') {
      this.state.reject(this.form.controls.displayName, {
        server: memberFailureMessage(reason),
      });
      return;
    }

    const control = reason === undefined ? undefined : CONTROL_FOR_REASON[reason];

    if (control === undefined) {
      this.state.error.set(memberFailureMessage(info.reason));
      return;
    }

    this.state.reject(this.form.controls[control], { server: memberFailureMessage(reason) });
  }
}

/**
 * Which control each contact refusal belongs to. The words are the table's; this is the mapping —
 * the same split `profile.ts` makes over the same five reasons.
 *
 * `Partial`, because `MemberFailure` also carries two reasons that belong to no contact field.
 */
const CONTROL_FOR_REASON: Partial<
  Record<MemberFailure['reason'], 'phoneNumber' | 'street' | 'houseNumber' | 'postalCode' | 'city'>
> = {
  invalid_phone: 'phoneNumber',
  invalid_street: 'street',
  invalid_house_number: 'houseNumber',
  invalid_postal_code: 'postalCode',
  invalid_city: 'city',
};
