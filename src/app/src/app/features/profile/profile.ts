import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthService } from '../../core/auth/auth.service';
import { ContactFailureReason } from '../../core/auth/auth.models';
import { changePasswordFailureMessage } from '../../core/auth/change-password-failure';
import { profileFailureMessage } from '../../core/auth/contact-failure';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import { ReadonlyField } from '../../shared/readonly-field/readonly-field';
import { createFormState } from '../../shared/forms/form-state';
import { Field } from '../../shared/forms/field/field';
import {
  MIN_PASSWORD_LENGTH,
  PHONE_PATTERN,
  POSTAL_CODE_PATTERN,
  passwordsMatch,
} from '../../core/auth/validation';

/**
 * The member's own account screen (S-13, FR-006 as rewritten).
 *
 * Name and email are rendered as TEXT, not as disabled inputs. A disabled input still looks like a
 * field that might become editable; plain text plus a hint says what is actually true — the gym owns
 * those two values and nothing in this app changes them.
 */
@Component({
  imports: [Field, ReactiveFormsModule, ReadonlyField],
  selector: 'app-profile',
  styleUrl: './profile.scss',
  templateUrl: './profile.html',
})
export class Profile {
  private readonly auth = inject(AuthService);

  protected readonly user = this.auth.user;

  /**
   * An account created before S-13 has NULL contact details. It is not an error state — the member
   * did nothing wrong — so the screen prompts rather than complains, and the form is empty and
   * ready rather than pre-filled with blanks it calls invalid.
   */
  protected readonly incomplete = computed(() => {
    const user = this.user();
    if (user === null) {
      return false;
    }

    // Truthiness, not `=== null`: the fields are optional as well as nullable on CurrentUser, and an
    // absent one means the same thing to a member as an empty one.
    return [user.phoneNumber, user.street, user.houseNumber, user.postalCode, user.city].some(
      (value) => !value,
    );
  });

  protected readonly form = inject(FormBuilder).nonNullable.group({
    phoneNumber: ['', [Validators.required, Validators.pattern(PHONE_PATTERN)]],
    street: ['', [Validators.required]],
    houseNumber: ['', [Validators.required]],
    postalCode: ['', [Validators.required, Validators.pattern(POSTAL_CODE_PATTERN)]],
    city: ['', [Validators.required]],
  });

  /**
   * TWO INDEPENDENT STATES, one per form group — the same reason the two groups are separate at all.
   * A rejected postal code must not disable the password button, and a wrong current password must
   * not make the address fields look broken.
   */
  protected readonly state = createFormState();
  protected readonly saved = signal(false);

  /** The contact table, exposed so a field says the same thing whoever caught the rule. */
  protected readonly contactMessage = profileFailureMessage;

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  /**
   * A SEPARATE FormGroup with its own submit and its own state, not a section of the one above.
   * The two forms fail independently — a rejected postal code must not disable the password button,
   * and a wrong current password must not make the address fields look broken.
   */
  protected readonly passwordForm = inject(FormBuilder).nonNullable.group(
    {
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(MIN_PASSWORD_LENGTH)]],
      confirmation: ['', [Validators.required]],
    },
    { validators: passwordsMatch },
  );

  protected readonly passwordState = createFormState();
  protected readonly passwordChanged = signal(false);

  /** The password table, exposed for the same reason `contactMessage` is. */
  protected readonly passwordMessage = changePasswordFailureMessage;

  constructor() {
    // Pre-filled from session state rather than from a GET: CurrentUser already carries these
    // fields, so the screen renders complete on first paint with no request of its own.
    const user = this.auth.user();
    if (user !== null) {
      this.form.patchValue({
        phoneNumber: user.phoneNumber ?? '',
        street: user.street ?? '',
        houseNumber: user.houseNumber ?? '',
        postalCode: user.postalCode ?? '',
        city: user.city ?? '',
      });
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.state.error.set(null);
    this.saved.set(false);
    this.state.submitting.set(true);

    try {
      const value = this.form.getRawValue();
      await this.auth.updateProfile({
        phoneNumber: value.phoneNumber.trim(),
        street: value.street.trim(),
        houseNumber: value.houseNumber.trim(),
        postalCode: value.postalCode.trim(),
        city: value.city.trim(),
      });

      // The service replaced the session signal from the response, so `incomplete` re-evaluates on
      // its own and the prompt disappears without anything here clearing it.
      this.saved.set(true);
    } catch (failure) {
      this.applyFailure(failure);
    } finally {
      this.state.submitting.set(false);
    }
  }

  /**
   * Changes the password without ending the session: the API refreshes this cookie against the
   * rotated security stamp, so nothing here has to re-establish anything. The form is reset on
   * success so the old values are not left sitting in the DOM.
   */
  protected async submitPassword(): Promise<void> {
    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      return;
    }

    this.passwordState.error.set(null);
    this.passwordChanged.set(false);
    this.passwordState.submitting.set(true);

    try {
      const { currentPassword, newPassword } = this.passwordForm.getRawValue();
      await this.auth.changePassword({ currentPassword, newPassword });

      this.passwordForm.reset();
      this.passwordChanged.set(true);
    } catch (failure) {
      const info = classifyFailure(failure);

      // A 429 or a 500 is not a wrong password and must not be shown under the password box.
      const transport = transportMessage(info);
      if (transport !== null) {
        this.passwordState.error.set(transport);
        return;
      }

      switch (info.reason) {
        case 'invalid_current_password':
          this.passwordState.reject(this.passwordForm.controls.currentPassword, {
            server: changePasswordFailureMessage(info.reason),
          });
          return;

        case 'invalid_new_password':
          this.passwordState.reject(this.passwordForm.controls.newPassword, {
            server: changePasswordFailureMessage(info.reason),
          });
          return;

        default:
          this.passwordState.error.set(changePasswordFailureMessage(info.reason));
      }
    } finally {
      this.passwordState.submitting.set(false);
    }
  }

  /** Convenience for the template: the group-level mismatch, once the member has touched the field. */
  protected get confirmationMismatch(): boolean {
    const group = this.passwordForm as FormGroup;
    return group.hasError('mismatch') && this.passwordForm.controls.confirmation.touched;
  }

  /**
   * The five contact reasons land on the five controls that produced them — outlet 1 of the rule in
   * AGENTS.md. The mapping is one-to-one, so a `switch` would be five identical branches; the
   * control name is derived from the reason instead, and the SENTENCE comes from the shared contact
   * table, which the admin's member form and the template's own client-side checks also read.
   */
  private applyFailure(failure: unknown): void {
    const info = classifyFailure(failure);

    // A 429, a 500 or a dead network belongs to no field — it goes in the banner as itself.
    const transport = transportMessage(info);
    if (transport !== null) {
      this.state.error.set(transport);
      return;
    }

    const control = CONTROL_FOR_REASON[info.reason as ContactFailureReason];

    if (control === undefined) {
      this.state.error.set(profileFailureMessage(info.reason));
      return;
    }

    this.state.reject(this.form.controls[control], {
      server: profileFailureMessage(info.reason),
    });
  }
}

/** Which control each contact refusal belongs to. The words are the table's; this is the mapping. */
const CONTROL_FOR_REASON: Record<
  ContactFailureReason,
  'phoneNumber' | 'street' | 'houseNumber' | 'postalCode' | 'city'
> = {
  invalid_phone: 'phoneNumber',
  invalid_street: 'street',
  invalid_house_number: 'houseNumber',
  invalid_postal_code: 'postalCode',
  invalid_city: 'city',
};
