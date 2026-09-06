import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ResetPasswordFailure } from '../../../core/auth/auth.models';
import { MIN_PASSWORD_LENGTH } from '../../../core/auth/validation';

/** Group-level for the reason profile.ts gives: on the control it would be wiped and flicker. */
function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const newPassword = group.get('newPassword')?.value;
  const confirmation = group.get('confirmation')?.value;

  return newPassword === confirmation ? null : { mismatch: true };
}

/**
 * Sets a new password from an emailed link (S-13). Public, guard-free.
 *
 * <p>
 * Both `email` and `token` come from the QUERY STRING, not the path. Identity's tokens are base64
 * and routinely contain '+' and '/', which a path segment mangles before Angular ever sees them —
 * PasswordResetNotification builds the link accordingly, and the two must stay in step.
 * </p>
 *
 * <p>
 * On success the member is sent to /login rather than signed in. The API deliberately does not
 * establish a session: signing in here would skip the one step that proves the new password works.
 * </p>
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-reset-password',
  styleUrl: './reset-password.scss',
  templateUrl: './reset-password.html',
})
export class ResetPassword {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  private readonly email: string;
  private readonly token: string;

  /**
   * A link with a missing parameter is dead on arrival — usually a mail client that wrapped the URL
   * and broke it. Detected before the form is offered, so the visitor is told to request a new link
   * instead of typing a password that could never be submitted.
   */
  protected readonly linkBroken: boolean;

  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(MIN_PASSWORD_LENGTH)]],
      confirmation: ['', [Validators.required]],
    },
    { validators: passwordsMatch },
  );

  /** True once the token is spent — a dead link, and the screen offers a way to get a fresh one. */
  protected readonly tokenRejected = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly submitting = signal(false);

  constructor() {
    const params = inject(ActivatedRoute).snapshot.queryParamMap;

    this.email = params.get('email') ?? '';
    this.token = params.get('token') ?? '';
    this.linkBroken = this.email === '' || this.token === '';

    // Scrubs the token out of the address bar and out of browser history as soon as it has been
    // read. It is a live credential until it is used, and this screen is often opened on a shared or
    // synced browser. replaceUrl so Back does not bring it back. The server-side exposure is not
    // solved here: the link arrives as an ordinary same-origin request, so the query string is
    // written to the request log before this code runs at all — that is accepted and recorded in the
    // implementation review (F4).
    void this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.submitting.set(true);

    try {
      await this.auth.resetPassword({
        email: this.email,
        token: this.token,
        newPassword: this.form.getRawValue().newPassword,
      });

      await this.router.navigate(['/login'], {
        queryParams: { reset: 'ok' },
      });
    } catch (failure) {
      const reason = ((failure as HttpErrorResponse)?.error as ResetPasswordFailure | undefined)
        ?.reason;

      switch (reason) {
        case 'invalid_token':
          // Not put on a control: no field the member can edit would fix it. The link is spent,
          // expired or wrong, and the only way forward is a new one.
          this.tokenRejected.set(true);
          return;

        case 'invalid_new_password':
          this.reject(this.form.controls.newPassword, { minlength: true });
          return;

        default:
          this.error.set('Nie udało się ustawić nowego hasła. Spróbuj ponownie za chwilę.');
      }
    } finally {
      this.submitting.set(false);
    }
  }

  protected get confirmationMismatch(): boolean {
    const group = this.form as FormGroup;
    return group.hasError('mismatch') && this.form.controls.confirmation.touched;
  }

  private reject(control: AbstractControl, errors: ValidationErrors): void {
    control.setErrors(errors);
    control.markAsTouched();
  }
}
