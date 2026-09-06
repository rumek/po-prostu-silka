import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';

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
  imports: [ReactiveFormsModule, RouterLink],
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
      await this.auth.forgotPassword({ email: this.form.getRawValue().email.trim() });

      // Set unconditionally on success. The API's 200 carries no information about the address, and
      // this must not appear to.
      this.sent.set(true);
    } catch {
      // A 429 from the rate limiter or a genuine outage both land here. Neither says anything about
      // the address — the message is about the request, not the account.
      this.error.set('Nie udało się wysłać wiadomości. Spróbuj ponownie za chwilę.');
    } finally {
      this.submitting.set(false);
    }
  }
}
