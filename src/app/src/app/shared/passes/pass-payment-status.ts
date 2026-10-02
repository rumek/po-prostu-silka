import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';

/**
 * Whether a karnet is paid, said the same way on every screen that shows one (pass-paid-flag): the
 * admin's karnet screen, the trainer's, and the member's card on Start.
 *
 * <p>
 * WORD ONLY. Unpaid is an outlined `.badge` reading "Nieopłacony" — a state, so a badge — and paid is
 * a quiet line with the day. No `--danger` (the style guide reserves it for errors and "Brak
 * miejsc"), and no glyph: there is no payment icon in `shared/icons`, and one more in the eager icon
 * primitive would cost bundle the app does not have to spend on a word that already says it.
 * </p>
 */
@Component({
  imports: [DatePipe],
  selector: 'app-pass-payment-status',
  styleUrl: './pass-payment-status.scss',
  template: `
    @if (paidAt(); as day) {
      <span class="pass-paid">Opłacony{{ showDate() ? ' · ' + (day | date: 'd MMM y') : '' }}</span>
    } @else {
      <span class="badge pass-unpaid">Nieopłacony</span>
    }
  `,
})
export class PassPaymentStatus {
  /** The karnet's `paidAt` — a `YYYY-MM-DD` day, or null for unpaid. */
  readonly paidAt = input.required<string | null>();

  /** Whether "Opłacony" carries its day. The member's card says only the word. */
  readonly showDate = input(true);
}
