import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { RegisterFailure } from '../../../core/auth/auth.models';
import { MIN_PASSWORD_LENGTH } from '../../../core/auth/validation';
import { ReadonlyField } from '../../../shared/readonly-field/readonly-field';

/**
 * Registration (FR-001, narrowed to invitation-only by S-17). The account is created ACTIVE and
 * signed in immediately (S-16, MP-03), so this always ends on the dashboard. It used to end on an
 * awaiting-approval screen; approval is gone and so is the screen.
 *
 * <p>
 * TWO FIELDS AND A CODE. The display name, the phone number and the four address fields left with
 * S-17: the club entered this person into its records before handing the invitation over, so asking
 * them to type it all again would only produce a second, competing copy. Whatever the club lacks,
 * the member fills in later on /profile, which already prompts for it.
 * </p>
 *
 * <p>
 * The screen is unreachable without an invitation — invitationGuard sees to that, and the API
 * refuses a codeless registration regardless.
 * </p>
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink, ReadonlyField],
  selector: 'app-register',
  styleUrl: './register.scss',
  templateUrl: './register.html',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly minPasswordLength = MIN_PASSWORD_LENGTH;

  /**
   * The code comes from the LINK, never from the keyboard (S-17, IR-03).
   *
   * NOT A FORM CONTROL, and deliberately so. It was a readonly input; a box the member cannot type
   * into is still a box, and it invites them to try. It is displayed as TEXT and carried straight
   * into the payload from here — which also removes the readonly-versus-disabled trap that a bound
   * control brought with it, since there is no longer a control whose value could be dropped.
   *
   * Read once from the snapshot: this route is not reused, so there is nothing to observe. The guard
   * has already refused an empty one, so by the time this runs in the browser there is a value —
   * the `?? ''` covers the prerender, where the guard deliberately decides nothing.
   */
  protected readonly invitationCode =
    inject(ActivatedRoute).snapshot.queryParamMap.get('invitationCode')?.trim() ?? '';

  protected readonly form = inject(FormBuilder).nonNullable.group({
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

      // Exactly three fields — the whole request contract since S-17.
      await this.auth.register({
        email: value.email.trim(),
        password: value.password,

        // Sent AS IT ARRIVED, dash and all: normalisation belongs to the API, which owns the
        // alphabet. Only the whitespace a URL or a paste brings along was stripped, on the way in.
        memberCode: this.invitationCode,
      });

      // Straight to the dashboard (S-16, MP-03): the account works the moment it exists, and since
      // S-17 it arrives holding the record the club has been keeping — bookings, karnet and plan
      // already on it.
      await this.router.navigate(['/']);
    } catch (failure) {
      await this.applyFailure(failure);
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * TWO KINDS OF FAILURE, AND THEY MUST NOT SHARE A HANDLER.
   *
   * <p>
   * A REFUSED CODE LEAVES THE SCREEN. The code is not a field at all, so there is nothing here for
   * the member to correct — an error message under text they cannot edit is a dead end. They go to
   * /login with a reason that screen renders, and the club issues a fresh invitation.
   * </p>
   *
   * <p>
   * EVERYTHING ELSE STAYS. `email_taken` and `invalid_password` are fixable right here, and
   * redirecting them would throw away a typed password for no reason. They land on the control that
   * caused them rather than in a banner — the payoff D8 bought with reactive forms.
   * </p>
   */
  private async applyFailure(failure: unknown): Promise<void> {
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

      // Both mean the invitation is no good, and neither is actionable on this screen. Collapsed
      // into one redirect on purpose — the member's next step is identical either way.
      case 'invalid_member_code':
      case 'unknown_member_code':
        await this.router.navigate(['/login'], {
          queryParams: { reason: 'invalid-invitation' },
        });
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
