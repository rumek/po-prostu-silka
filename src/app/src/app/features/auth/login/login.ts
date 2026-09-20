import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { loginFailureMessage } from '../../../core/auth/login-failure';
import { Field } from '../../../shared/forms/field/field';

/**
 * Sign-in. Reactive forms (S-01 D8) — this decides the idiom for the project: server-returned field
 * errors map onto controls, and validation is testable without a DOM.
 */
@Component({
  imports: [Field, ReactiveFormsModule, RouterLink],
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

  /** Read once from the snapshot: this route is not reused, so there is nothing to observe. */
  private readonly route = inject(ActivatedRoute);

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

/**
 * BANNER ONLY, PERMANENTLY — outlet 2 of the rule in AGENTS.md, and never outlet 1.
 *
 * A message under the e-mail box would say "this address is the part that was wrong" just as
 * loudly as the words would. The API refuses to distinguish a wrong password from an unknown
 * address, so this screen must not imply it can. `login.spec.ts` pins it.
 *
 * A rate-limited or offline sign-in now reads as itself rather than as a failed credential check —
 * /login is the one endpoint with a limiter in front of it (`src/Api/Program.cs:150`), so this is
 * the screen where a 429 was most likely and least explicable.
 */
function messageFor(failure: unknown): string {
  const info = classifyFailure(failure);

  return transportMessage(info) ?? loginFailureMessage(info.reason);
}
