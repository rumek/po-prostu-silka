import { FormControl, Validators } from '@angular/forms';
import { createFormState } from './form-state';

describe('createFormState', () => {
  it('starts idle, unfailed and silent', () => {
    const state = createFormState();

    expect(state.loading()).toBe(false);
    expect(state.loadFailed()).toBe(false);
    expect(state.submitting()).toBe(false);
    expect(state.error()).toBeNull();
  });

  it('gives each call its own signals', () => {
    // profile.ts holds two of these, one per form group. Sharing signals between them would make
    // a failed password change blank the contact-details form.
    const first = createFormState();
    const second = createFormState();

    first.submitting.set(true);

    expect(second.submitting()).toBe(false);
  });

  /**
   * `markAsTouched` IS THE HALF THAT IS EASY TO DROP. The templates reveal a field error only once
   * the control is touched, and a submit of an otherwise-valid form never touches anything — so
   * `setErrors` alone produces a form that refused the user and said nothing about it.
   */
  it('rejects a control by setting the errors AND marking it touched', () => {
    const state = createFormState();
    const control = new FormControl('taken@example.com');

    expect(control.touched).toBe(false);

    state.reject(control, { emailTaken: true });

    expect(control.errors).toEqual({ emailTaken: true });
    expect(control.touched).toBe(true);
    expect(control.valid).toBe(false);
  });

  it('replaces whatever validation errors the control already carried', () => {
    const control = new FormControl('', { validators: Validators.required });
    control.updateValueAndValidity();
    expect(control.errors).toEqual({ required: true });

    createFormState().reject(control, { serverSaysNo: true });

    expect(control.errors).toEqual({ serverSaysNo: true });
  });
});
