import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ClassTypeService } from '../../../core/scheduling/class-type.service';
import { ClassTypeFailure } from '../../../core/scheduling/class-type.models';

/** Matches the server's floor in ClassTypeEndpoints.Validate. Keep the two in step. */
const MIN_DURATION = 1;

/** Matches the server's ceiling in ClassTypeEndpoints.Validate. Keep the two in step. */
const MAX_DURATION = 480;

/** Matches the server's floor in ClassTypeEndpoints.Validate. Keep the two in step. */
const MIN_CAPACITY = 1;

/** Matches the server's ceiling in ClassTypeEndpoints.Validate. Keep the two in step. */
const MAX_CAPACITY = 200;

/** Matches ClassTypeConfiguration's column length and the server's check. Keep all three in step. */
const MAX_DESCRIPTION = 1000;

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
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-class-type-form',
  styleUrl: './class-type-form.scss',
  templateUrl: './class-type-form.html',
})
export class ClassTypeForm implements OnInit {
  private readonly classTypes = inject(ClassTypeService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly maxDescription = MAX_DESCRIPTION;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required]],
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

  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly submitting = signal(false);

  /** A form-level message, for failures that belong to no single control. */
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }

    this.editingId.set(id);
    this.loading.set(true);

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
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      // Reveal the errors rather than silently doing nothing.
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.submitting.set(true);

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
      this.submitting.set(false);
    }
  }

  /**
   * Maps a server refusal onto the control responsible for it, so the admin sees what to change.
   * Follows class-form.ts's applyFailure/reject pair.
   */
  private applyFailure(failure: unknown): void {
    const reason = ((failure as HttpErrorResponse)?.error as ClassTypeFailure | undefined)?.reason;

    switch (reason) {
      case 'name_taken':
        this.reject(this.form.controls.name, { nameTaken: true });
        return;
      case 'invalid_duration':
        this.reject(this.form.controls.defaultDurationMinutes, { min: true });
        return;
      case 'invalid_capacity':
        this.reject(this.form.controls.defaultCapacity, { min: true });
        return;
      case 'description_too_long':
        this.reject(this.form.controls.description, { maxlength: true });
        return;
      case 'missing_field':
        this.form.markAllAsTouched();
        this.error.set('Uzupełnij wszystkie wymagane pola.');
        return;
      default:
        this.error.set('Nie udało się zapisać typu zajęć. Spróbuj ponownie za chwilę.');
    }
  }

  private reject(control: AbstractControl, errors: ValidationErrors): void {
    control.setErrors(errors);
    // Required: the template only reveals errors on touched controls.
    control.markAsTouched();
  }
}
