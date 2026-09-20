import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { createFormState } from '../../../shared/forms/form-state';
import { Field } from '../../../shared/forms/field/field';

/**
 * Asks for a reset link (S-13). Public, guard-free, reachable from the login screen.
 *
 * <p>
 * THIS SCREEN NEVER TELLS THE VISITOR WHETHER THE ADDRESS EXISTS. The API answers identically for a
 * registered and an unregistered address, and this component must not undo that: there is exactly
 * one success path, it shows one neutral message, and nothing branches on anything the API returned
 * (it returns nothing). Do not add a "we couldn't find that address" state — it would hand an
 * anonymous visitor the account-enumeration oracle the endpoint was shaped to deny.
 * </p>
 */
@Component({
  imports: [Field, ReactiveFormsModule, RouterLink],
  selector: 'app-forgot-password',
  styleUrl: './forgot-password.scss',
  templateUrl: './forgot-password.html',
})
export class ForgotPassword {
  private readonly auth = inject(AuthService);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  /** Replaces the form once submitted, so the visitor is not invited to send a second time. */
  protected readonly sent = signal(false);

  protected readonly state = createFormState();

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.state.error.set(null);
    this.state.submitting.set(true);

    try {
      await this.auth.forgotPassword({ email: this.form.getRawValue().email.trim() });

      // Set unconditionally on success. The API's 200 carries no information about the address, and
      // this must not appear to.
      this.sent.set(true);
    } catch (failure) {
      // A 429 from the rate limiter and a genuine outage both land here, and S-19 is what lets them
      // read as themselves — this endpoint is behind a limiter, so "zbyt wiele prób" is the likeliest
      // answer and used to be indistinguishable from an outage. Neither says anything about the
      // address: every one of these sentences is about the REQUEST, not the account, which is what
      // keeps the non-disclosure above intact.
      //
      // There is no union here — the endpoint names no reasons — so `business` cannot occur and the
      // `??` arm is unreachable belt-and-braces.
      this.state.error.set(
        transportMessage(classifyFailure(failure)) ??
          'Nie udało się wysłać wiadomości. Spróbuj ponownie za chwilę.',
      );
    } finally {
      this.state.submitting.set(false);
    }
  }
}
