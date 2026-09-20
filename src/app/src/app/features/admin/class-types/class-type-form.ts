import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ClassTypeService } from '../../../core/scheduling/class-type.service';
import { CLASS_TYPE_BOUNDS, ClassTypeFailure } from '../../../core/scheduling/class-type.models';
import { classTypeFailureMessage } from '../../../core/scheduling/class-type-failure';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { createFormState } from '../../../shared/forms/form-state';
import { Field } from '../../../shared/forms/field/field';

/**
 * The bounds, now shared with `class-type-failure.ts` (S-19).
 *
 * They were `const`s here, and the refusal sentences that quote them lived in this file too — so
 * the pair could not drift. The sentences moved to the table, so the numbers moved beside the union
 * they belong to. `CLASS_TYPE_BOUNDS` mirrors ClassTypeEndpoints.Validate; keep them in step.
 */
const {
  minDuration: MIN_DURATION,
  maxDuration: MAX_DURATION,
  minCapacity: MIN_CAPACITY,
  maxCapacity: MAX_CAPACITY,
  maxName: MAX_NAME,
  maxDescription: MAX_DESCRIPTION,
} = CLASS_TYPE_BOUNDS;

/**
 * Create and edit a class type (prd-v2 FR-004, FR-005), in one component distinguished by the route
 * parameter — the same shape as ClassForm.
 *
 * Server failures land on the CONTROL they belong to, following the register and class screens:
 * name_taken on the name field, the range refusals on their numeric fields. A banner would make the
 * admin hunt for which field to change.
 *
 * There is no activation control here. Activation has its own endpoints and lives on the list, so a
 * careless edit cannot resurrect a type the admin retired.
 */
@Component({
  imports: [Field, ReactiveFormsModule, RouterLink],
  selector: 'app-class-type-form',
  styleUrl: './class-type-form.scss',
  templateUrl: './class-type-form.html',
})
export class ClassTypeForm implements OnInit {
  private readonly classTypes = inject(ClassTypeService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly maxName = MAX_NAME;
  protected readonly maxDescription = MAX_DESCRIPTION;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(MAX_NAME)]],
    // Optional — the one field here that may legitimately be empty.
    description: ['', [Validators.maxLength(MAX_DESCRIPTION)]],
    defaultDurationMinutes: [
      60,
      [Validators.required, Validators.min(MIN_DURATION), Validators.max(MAX_DURATION)],
    ],
    defaultCapacity: [
      12,
      [Validators.required, Validators.min(MIN_CAPACITY), Validators.max(MAX_CAPACITY)],
    ],
  });

  /** Null when creating; the type id when editing. Drives the title, the verb and the endpoint. */
  protected readonly editingId = signal<string | null>(null);

  protected readonly state = createFormState();

  /** The table, exposed so a field says the same thing whoever caught the rule. */
  protected readonly failureMessage = classTypeFailureMessage;

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }

    this.editingId.set(id);
    this.state.loading.set(true);

    try {
      const existing = await this.classTypes.getById(id);

      this.form.setValue({
        name: existing.name,
        // The API's "absent" is null; the form's is an empty string.
        description: existing.description ?? '',
        defaultDurationMinutes: existing.defaultDurationMinutes,
        defaultCapacity: existing.defaultCapacity,
      });
    } catch {
      this.state.loadFailed.set(true);
    } finally {
      this.state.loading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      // Reveal the errors rather than silently doing nothing.
      this.form.markAllAsTouched();
      return;
    }

    this.state.error.set(null);
    this.state.submitting.set(true);

    const value = this.form.getRawValue();
    const description = value.description.trim();

    const request = {
      name: value.name,
      // Collapse blank back to the API's single representation of "absent".
      description: description.length === 0 ? null : description,
      defaultDurationMinutes: value.defaultDurationMinutes,
      defaultCapacity: value.defaultCapacity,
    };

    try {
      const id = this.editingId();
      if (id) {
        await this.classTypes.update(id, request);
      } else {
        await this.classTypes.create(request);
      }

      await this.router.navigate(['/admin/class-types']);
    } catch (failure) {
      this.applyFailure(failure);
    } finally {
      this.state.submitting.set(false);
    }
  }

  /**
   * Maps a server refusal onto the control responsible for it, so the admin sees what to change.
   * Follows class-form.ts's applyFailure/reject pair.
   */
  private applyFailure(failure: unknown): void {
    const info = classifyFailure(failure);

    // A 429, a 500 or a dead network belongs to no control.
    const transport = transportMessage(info);
    if (transport !== null) {
      this.state.error.set(transport);
      return;
    }

    const reason = info.reason as ClassTypeFailure['reason'] | undefined;
    const message = classTypeFailureMessage(reason);

    switch (reason) {
      case 'name_taken':
      case 'name_too_long':
        this.state.reject(this.form.controls.name, { server: message });
        return;
      case 'invalid_duration':
        this.state.reject(this.form.controls.defaultDurationMinutes, { server: message });
        return;
      case 'invalid_capacity':
        this.state.reject(this.form.controls.defaultCapacity, { server: message });
        return;
      case 'description_too_long':
        this.state.reject(this.form.controls.description, { server: message });
        return;
      case 'missing_field':
        this.form.markAllAsTouched();
        this.state.error.set(message);
        return;
      default:
        this.state.error.set(message);
    }
  }
}
