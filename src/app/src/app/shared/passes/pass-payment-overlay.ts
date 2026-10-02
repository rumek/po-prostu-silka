import { DatePipe } from '@angular/common';
import { Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MembershipPassView } from '../../core/admin/member-admin.models';
import { clubToday } from '../../core/passes/club-today';
import { Field } from '../forms/field/field';
import { useOverlayFocus } from '../forms/overlay-focus';
import { OverlayHead } from '../overlay/overlay-head';

/**
 * "Oznacz jako opłacony" — the one overlay both the admin's and the trainer's karnet screens open to
 * record a payment (pass-paid-flag). In `shared/` because two screens use it; a third copy is the
 * review finding S-19 warns about.
 *
 * <h2>It renders and reports</h2>
 *
 * Like the schedule's overlays, it performs no request: it emits the chosen day, and the screen
 * makes the call and decides when it closes. A refusal comes back through {@link failure} and is shown
 * as a banner inside the panel (outlet 2) — the overlay is a one-field form, and the date the admin
 * would correct is right there.
 *
 * The day defaults to the club-local today and cannot be set later (`max`); the server refuses a
 * future or absurdly old day with `invalid_paid_at` anyway, which is what {@link failure} carries.
 */
@Component({
  host: { '(document:keydown.escape)': 'close()' },
  imports: [DatePipe, Field, FormsModule, OverlayHead],
  selector: 'app-pass-payment-overlay',
  styleUrl: './pass-payment-overlay.scss',
  templateUrl: './pass-payment-overlay.html',
})
export class PassPaymentOverlay {
  private readonly focus = useOverlayFocus(() => this.close());

  readonly pass = input.required<MembershipPassView>();

  /** The request is in flight — Zapisz is disabled so it cannot be sent twice. */
  readonly busy = input(false);

  /** Why the last attempt was refused, in the screen's words (never this template's). */
  readonly failure = input<string | null>(null);

  /** The chosen payment day, `YYYY-MM-DD`. */
  readonly confirmed = output<string>();
  readonly closed = output<void>();

  protected readonly today = clubToday();
  protected readonly paidAt = signal(this.today);

  protected confirm(): void {
    if (this.paidAt()) {
      this.confirmed.emit(this.paidAt());
    }
  }

  protected close(): void {
    this.closed.emit();
  }
}
