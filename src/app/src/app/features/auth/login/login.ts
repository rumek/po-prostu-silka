import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { LoginFailure } from '../../../core/auth/auth.models';

/**
 * Sign-in. Reactive forms (S-01 D8) — this decides the idiom for the project: server-returned field
 * errors map onto controls, and validation is testable without a DOM.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-login',
  styleUrl: './login.scss',
  templateUrl: './login.html',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected readonly error = signal<string | null>(null);
  protected readonly submitting = signal(false);

  /**
   * Set by /reset-password on success (S-13). The reset endpoint deliberately does not sign the
   * member in, so this screen is where they land and where they find out it worked — otherwise the
   * flow ends on a bare login form that looks like nothing happened.
   *
   * Read once from the snapshot: this route is not reused, so there is nothing to observe.
   */
  private readonly route = inject(ActivatedRoute);

  protected readonly passwordReset = this.route.snapshot.queryParamMap.get('reset') === 'ok';

  /**
   * Set by the register screen when the API refused the invitation (S-17).
   *
   * The code field there is readonly, so a refused code leaves the member nothing to correct and
   * they are sent here instead. WITHOUT THIS THE REDIRECT IS SILENT — they would arrive at a login
   * form with no idea why, and try the same dead link again.
   *
   * It says no more than the API does. "Used", "expired" and "revoked" are one answer on the wire
   * for the account-enumeration reason, and the message must not imply the screen can tell them
   * apart.
   */
  protected readonly invalidInvitation =
    this.route.snapshot.queryParamMap.get('reason') === 'invalid-invitation';

  protected async submit(): Promise<void> {
    // markAllAsTouched, not a silent return: a submit that appears to do nothing reads as a broken
    // button. Touching the controls is what reveals the messages.
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.submitting.set(true);

    try {
      // The returned user is no longer read here: since S-16 there is only one destination, so
      // nothing branches on the status. AuthService still holds it for the rest of the app.
      await this.auth.login(this.form.getRawValue());

      // Straight to the dashboard (S-16, MP-03). Login used to route by status, because a pending
      // member signed in successfully and belonged on the awaiting-approval screen; approval is gone
      // and so is that screen. A BLOCKED account never reaches this line at all — the API refuses
      // the login itself rather than issuing a cookie.
      await this.router.navigate(['/']);
    } catch (failure) {
      this.error.set(messageFor(failure));
    } finally {
      this.submitting.set(false);
    }
  }
}

function messageFor(failure: unknown): string {
  const reason = (failure as HttpErrorResponse)?.error as LoginFailure | undefined;

  switch (reason?.reason) {
    case 'blocked':
      return 'Twoje konto zostało zablokowane. Skontaktuj się z obsługą siłowni.';

    // One message for a wrong password AND an unknown address. The API deliberately does not
    // distinguish them, so saying "nie ma takiego konta" here would leak what it refuses to.
    case 'invalid_credentials':
      return 'Nieprawidłowy e-mail lub hasło.';

    default:
      return 'Nie udało się zalogować. Spróbuj ponownie za chwilę.';
  }
}
