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

/** Matches Identity's RequiredLength in src/Program.cs. Keep the two in step. */
const MIN_PASSWORD_LENGTH = 8;

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
      await this.auth.register({ ...value, displayName: value.displayName.trim() });

      await this.router.navigate(['/pending']);
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
