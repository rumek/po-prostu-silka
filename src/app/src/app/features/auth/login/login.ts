import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
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
      const user = await this.auth.login(this.form.getRawValue());

      // Route by status, not by a login failure — since S-01 a pending member signs in successfully
      // and simply belongs on a different screen.
      await this.router.navigate([user.status === 'Active' ? '/' : '/pending']);
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
