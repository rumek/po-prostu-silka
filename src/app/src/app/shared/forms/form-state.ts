import { signal, WritableSignal } from '@angular/core';
import { AbstractControl, ValidationErrors } from '@angular/forms';

/** The four signals every form screen in this app declares, plus the one helper they all copied. */
export interface FormState {
  /** The initial load is in flight. */
  readonly loading: WritableSignal<boolean>;
  /** The initial load failed, so there is no form to show. */
  readonly loadFailed: WritableSignal<boolean>;
  /** A submit is in flight. */
  readonly submitting: WritableSignal<boolean>;
  /** A form-level message, for failures that belong to no single control. */
  readonly error: WritableSignal<string | null>;

  /**
   * Puts a refusal on the control that caused it.
   *
   * `markAsTouched` is not optional. The templates reveal a field error only once the control is
   * touched — which a submit of an otherwise-valid form never does — so `setErrors` alone leaves
   * the user staring at a form that refused them and said nothing.
   */
  reject(control: AbstractControl, errors: ValidationErrors): void;
}

/**
 * The four-signal block and `reject()`, written once (S-19, CS-06).
 *
 * <h2>A function, not a base class</h2>
 *
 * Seven components carried a byte-identical `reject()` and most of them the same four signals.
 * The obvious-looking fix — a `FormComponent` to extend — is the wrong one here: this repo uses
 * no component inheritance anywhere, every shared component is standalone, and a base class would
 * also have to own the `inject()` calls and the lifecycle of whatever it inherited into. A field
 * holding the result of this function composes instead, and a component that needs only two of the
 * four signals simply ignores the others.
 *
 * Components hold it as `protected readonly state = createFormState()` and the template reads
 * `state.loading()`, `state.error()` and so on.
 */
export function createFormState(): FormState {
  return {
    loading: signal(false),
    loadFailed: signal(false),
    submitting: signal(false),
    error: signal<string | null>(null),

    reject(control: AbstractControl, errors: ValidationErrors): void {
      control.setErrors(errors);
      control.markAsTouched();
    },
  };
}
