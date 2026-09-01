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
import { ClassService } from '../../../core/scheduling/class.service';
import { ClassFailure } from '../../../core/scheduling/class.models';
import { fromLocalInputValue, toLocalInputValue } from '../../../core/scheduling/local-datetime';

/** Matches the server's floor in ClassEndpoints.Validate. Keep the two in step. */
const MIN_CAPACITY = 1;

/** Matches the server's floor in ClassEndpoints.Validate. Keep the two in step. */
const MIN_DURATION = 1;

/**
 * Create and edit a class (FR-011), in one component distinguished by the route parameter.
 *
 * The app's first parameterized route and its first date/time and number inputs — S-04 and S-07 will
 * both inherit these conventions.
 *
 * `datetime-local` carries no timezone: its value is a bare wall-clock reading. Conversion in both
 * directions goes through local-datetime.ts, because getting it backwards shifts every saved class
 * by the local offset without anything failing.
 *
 * Server failures land on the CONTROL they belong to, following the register screen: room_conflict
 * on the room field, starts_in_past on the start field. A banner would make the admin hunt for which
 * field to change.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-class-form',
  styleUrl: './class-form.scss',
  templateUrl: './class-form.html',
})
export class ClassForm implements OnInit {
  private readonly classes = inject(ClassService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required]],
    startsAt: ['', [Validators.required]],
    durationMinutes: [60, [Validators.required, Validators.min(MIN_DURATION)]],
    room: ['', [Validators.required]],
    instructor: ['', [Validators.required]],
    capacity: [12, [Validators.required, Validators.min(MIN_CAPACITY)]],
  });

  /** Null when creating; the class id when editing. Drives the title, the verb and the endpoint. */
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
      const existing = await this.classes.getById(id);

      this.form.setValue({
        name: existing.name,
        // UTC instant -> the local wall clock the input displays.
        startsAt: toLocalInputValue(existing.startsAt),
        durationMinutes: existing.durationMinutes,
        room: existing.room,
        instructor: existing.instructor,
        capacity: existing.capacity,
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
    const request = {
      name: value.name,
      // The local wall clock the admin typed -> the UTC instant the API stores.
      startsAt: fromLocalInputValue(value.startsAt),
      durationMinutes: value.durationMinutes,
      room: value.room,
      instructor: value.instructor,
      capacity: value.capacity,
    };

    try {
      const id = this.editingId();
      if (id) {
        await this.classes.update(id, request);
      } else {
        await this.classes.create(request);
      }

      await this.router.navigate(['/admin/classes']);
    } catch (failure) {
      this.applyFailure(failure);
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * Maps a server refusal onto the control responsible for it, so the admin sees what to change.
   * Follows register.ts's applyFailure/reject pair.
   */
  private applyFailure(failure: unknown): void {
    const reason = ((failure as HttpErrorResponse)?.error as ClassFailure | undefined)?.reason;

    switch (reason) {
      case 'room_conflict':
        this.reject(this.form.controls.room, { roomConflict: true });
        return;
      case 'starts_in_past':
        this.reject(this.form.controls.startsAt, { startsInPast: true });
        return;
      case 'invalid_capacity':
        this.reject(this.form.controls.capacity, { min: true });
        return;
      case 'invalid_duration':
        this.reject(this.form.controls.durationMinutes, { min: true });
        return;
      case 'missing_field':
        this.form.markAllAsTouched();
        this.error.set('Uzupełnij wszystkie pola.');
        return;
      default:
        this.error.set('Nie udało się zapisać zajęć. Spróbuj ponownie za chwilę.');
    }
  }

  private reject(control: AbstractControl, errors: ValidationErrors): void {
    control.setErrors(errors);
    // Required: the template only reveals errors on touched controls.
    control.markAsTouched();
  }
}
