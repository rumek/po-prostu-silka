import { formatDate } from '@angular/common';
import { LOCALE_ID, WritableSignal, inject, signal } from '@angular/core';
import { MembershipPassView } from '../../core/admin/member-admin.models';
import { membershipPassFailureMessage } from '../../core/admin/membership-pass-failure';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import { PassPaymentService } from '../../core/passes/pass-payment.service';
import { BusySet, createBusySet } from '../forms/busy-set';
import { ToastService } from '../toast/toast.service';

/** The payment state and actions a karnet screen binds to — see {@link createPassPaymentActions}. */
export interface PassPaymentActions {
  /** The karnet the "Oznacz jako opłacony" overlay is open for, or null while it is closed. */
  readonly paying: WritableSignal<MembershipPassView | null>;
  /** The overlay's Zapisz is in flight. */
  readonly busy: WritableSignal<boolean>;
  /** The overlay's banner: a refused payment stays inside the form that made it (outlet 2). */
  readonly failure: WritableSignal<string | null>;
  /** Rows with a "Cofnij płatność" in flight, so one slow row does not disable the others. */
  readonly rows: BusySet;

  open(pass: MembershipPassView): void;
  close(): void;
  /** The overlay's Zapisz. */
  markPaid(paidAt: string): Promise<void>;
  /**
   * "Cofnij płatność". This is a row action, so its outcome is a toast (outlet 3). The toast names the
   * day that was cleared, because nothing else keeps it, and re-marking the karnet needs that day.
   */
  clearPaid(pass: MembershipPassView): Promise<void>;
}

/**
 * The karnet payment actions, written once (pass-paid-flag). They are shared by the admin's karnet
 * screen and the trainer's, which must behave identically. Two copies would drift, and a third would
 * be the copy S-19 counts as a finding.
 *
 * A FUNCTION held in a field, like `createFormState`, never a base class: this repo uses no component
 * inheritance. Call it in an injection context, which in practice means a field initializer.
 *
 * `replace` receives the view the payment write returned. Patching the held row with it is safe:
 * payment touches no entry, and the returned entry counts were read in the same request.
 */
export function createPassPaymentActions(
  replace: (view: MembershipPassView) => void,
): PassPaymentActions {
  const payments = inject(PassPaymentService);
  const toast = inject(ToastService);
  const locale = inject(LOCALE_ID);

  const paying = signal<MembershipPassView | null>(null);
  const busy = signal(false);
  const failure = signal<string | null>(null);
  const rows = createBusySet();

  const close = (): void => {
    paying.set(null);
    failure.set(null);
  };

  return {
    paying,
    busy,
    failure,
    rows,

    open(pass: MembershipPassView): void {
      failure.set(null);
      paying.set(pass);
    },

    close,

    async markPaid(paidAt: string): Promise<void> {
      const pass = paying();
      if (pass === null) {
        return;
      }

      busy.set(true);
      failure.set(null);

      try {
        replace(await payments.setPaid(pass.id, paidAt));
        close();
        toast.success('Karnet oznaczony jako opłacony.');
      } catch (refusal) {
        failure.set(messageFor(refusal));
      } finally {
        busy.set(false);
      }
    },

    async clearPaid(pass: MembershipPassView): Promise<void> {
      rows.setBusy(pass.id, true);

      try {
        replace(await payments.setPaid(pass.id, null));
        toast.success(
          pass.paidAt === null
            ? 'Cofnięto płatność.'
            : `Cofnięto płatność z ${formatDate(pass.paidAt, 'd MMM y', locale)}.`,
        );
      } catch (refusal) {
        toast.error(messageFor(refusal));
      } finally {
        rows.setBusy(pass.id, false);
      }
    },
  };
}

/** Transport words first (a 500 is not a karnet rule), then the karnet table's. */
function messageFor(failure: unknown): string {
  const info = classifyFailure(failure);

  return transportMessage(info) ?? membershipPassFailureMessage(info.reason);
}
