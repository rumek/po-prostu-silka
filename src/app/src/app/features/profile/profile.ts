import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { AuthService } from '../../core/auth/auth.service';
import { ProfileFailure } from '../../core/auth/auth.models';

/**
 * Mirrors the rules in src/Application/Members/ContactDetails.cs, and must stay identical to the
 * copies in the register component — the two forms write the same five columns through the same
 * server-side helper, so a rule that differs here is a field the member can save but not register
 * with.
 */
const POSTAL_CODE_PATTERN = /^\d{2}-\d{3}$/;
const PHONE_PATTERN = /^(?:\+?48[\s-]?)?(?:\d[\s-]?){8}\d$/;

/**
 * The member's own account screen (S-13, FR-006 as rewritten).
 *
 * Name and email are rendered as TEXT, not as disabled inputs. A disabled input still looks like a
 * field that might become editable; plain text plus a hint says what is actually true — the gym owns
 * those two values and nothing in this app changes them.
 */
@Component({
  imports: [ReactiveFormsModule],
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

  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal(false);
  protected readonly submitting = signal(false);

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

    this.error.set(null);
    this.saved.set(false);
    this.submitting.set(true);

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
      this.submitting.set(false);
    }
  }

  /** Same per-control mapping the register screen uses, over the same five reason codes. */
  private applyFailure(failure: unknown): void {
    const reason = ((failure as HttpErrorResponse)?.error as ProfileFailure | undefined)?.reason;

    switch (reason) {
      case 'invalid_phone':
        this.reject(this.form.controls.phoneNumber, { pattern: true });
        return;

      case 'invalid_street':
        this.reject(this.form.controls.street, { required: true });
        return;

      case 'invalid_house_number':
        this.reject(this.form.controls.houseNumber, { required: true });
        return;

      case 'invalid_postal_code':
        this.reject(this.form.controls.postalCode, { pattern: true });
        return;

      case 'invalid_city':
        this.reject(this.form.controls.city, { required: true });
        return;

      default:
        this.error.set('Nie udało się zapisać danych. Spróbuj ponownie za chwilę.');
    }
  }

  /** markAsTouched is not optional — see the identical helper in the register component. */
  private reject(control: AbstractControl, errors: ValidationErrors): void {
    control.setErrors(errors);
    control.markAsTouched();
  }
}
