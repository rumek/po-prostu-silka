import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { RegisterFailure } from '../../../core/auth/auth.models';
import {
  MIN_PASSWORD_LENGTH,
  PHONE_PATTERN,
  POSTAL_CODE_PATTERN,
} from '../../../core/auth/validation';

/**
 * Registration (FR-001). The account is created Pending and signed in immediately, so this always
 * ends on the awaiting-approval screen.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-register',
  styleUrl: './register.scss',
  templateUrl: './register.html',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    displayName: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(MIN_PASSWORD_LENGTH)]],
    phoneNumber: ['', [Validators.required, Validators.pattern(PHONE_PATTERN)]],
    street: ['', [Validators.required]],
    houseNumber: ['', [Validators.required]],
    postalCode: ['', [Validators.required, Validators.pattern(POSTAL_CODE_PATTERN)]],
    city: ['', [Validators.required]],

    // OPTIONAL, and no client-side format rule beyond a length bound: the server normalises what is
    // typed (case, the dash it printed, stray spaces), so anything stricter here would reject codes
    // the API would have accepted.
    memberCode: ['', [Validators.maxLength(32)]],
  });

  protected readonly error = signal<string | null>(null);
  protected readonly submitting = signal(false);

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.submitting.set(true);

    try {
      const value = this.form.getRawValue();

      // Trimmed here as well as on the server: the API normalises before storing, but a trailing
      // space the member cannot see should not be what a validator rejects on the way back.
      await this.auth.register({
        ...value,
        displayName: value.displayName.trim(),
        phoneNumber: value.phoneNumber.trim(),
        street: value.street.trim(),
        houseNumber: value.houseNumber.trim(),
        postalCode: value.postalCode.trim(),
        city: value.city.trim(),

        // Omitted rather than sent empty. The server treats a blank code as "no code", but sending
        // one would put an empty string into the request for every member who has never seen a code.
        memberCode: value.memberCode.trim() || null,
      });

      // Straight to the dashboard (S-16, MP-03): the account works the moment it exists, so there is
      // nothing to wait on. What the new member sees there is an empty karnet card telling them to
      // speak to reception, which is the honest next step.
      await this.router.navigate(['/']);
    } catch (failure) {
      this.applyFailure(failure);
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * `email_taken` goes onto the email CONTROL rather than into the banner — this is the payoff D8
   * bought with reactive forms. The member sees the problem next to the field that caused it, and
   * the error clears itself as soon as they change the address.
   */
  private applyFailure(failure: unknown): void {
    const reason = ((failure as HttpErrorResponse)?.error as RegisterFailure | undefined)?.reason;

    switch (reason) {
      case 'email_taken':
        this.reject(this.form.controls.email, { emailTaken: true });
        return;

      case 'invalid_email':
        this.reject(this.form.controls.email, { email: true });
        return;

      case 'invalid_password':
        this.reject(this.form.controls.password, { minlength: true });
        return;

      case 'invalid_display_name':
        this.reject(this.form.controls.displayName, { required: true });
        return;

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

      // Both land on the code field rather than in the page-level banner: the member typed something
      // wrong in one specific box and that is where they will look. The MESSAGES differ (the template
      // branches on which error is set) because "that is not a code" and "that code no longer works"
      // ask for different things from the person reading them.
      case 'invalid_member_code':
        this.reject(this.form.controls.memberCode, { pattern: true });
        return;

      case 'unknown_member_code':
        this.reject(this.form.controls.memberCode, { unknown: true });
        return;

      default:
        this.error.set('Nie udało się utworzyć konta. Spróbuj ponownie za chwilę.');
    }
  }

  /**
   * markAsTouched is not optional here. The template reveals a field error only once the control is
   * touched — which a submit of an otherwise-valid form never does — so setErrors alone would leave
   * the member staring at a form that refused them and said nothing.
   */
  private reject(control: AbstractControl, errors: ValidationErrors): void {
    control.setErrors(errors);
    control.markAsTouched();
  }
}
