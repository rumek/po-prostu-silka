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
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import { TrainerSummary } from '../../../core/admin/member-admin.models';
import { ClassService } from '../../../core/scheduling/class.service';
import { ClassTypeService } from '../../../core/scheduling/class-type.service';
import { ClassFailure } from '../../../core/scheduling/class.models';
import { ClassTypeSummary } from '../../../core/scheduling/class-type.models';
import { classFailureMessage } from '../../../core/scheduling/class-failure';
import { fromLocalInputValue, toLocalInputValue } from '../../../core/scheduling/local-datetime';

/** Matches the server's bounds in ClassEndpoints.Validate. Keep the two in step. */
export const MIN_CAPACITY = 1;
export const MAX_CAPACITY = 200;

/** Matches the server's bounds in ClassEndpoints.Validate. Keep the two in step. */
export const MIN_DURATION = 1;
export const MAX_DURATION = 480;

/**
 * Create and edit a class occurrence (prd-v2 US-01), in one component distinguished by the route
 * parameter.
 *
 * <p>
 * A FORM OF SELECTIONS. Since S-06 there is no name, no room and no typed instructor: the admin
 * picks a class type and a trainer, and the two numbers arrive PREFILLED from the type's defaults.
 * That prefill is the whole reason the definition layer exists — it is what removes the retyping.
 * </p>
 *
 * <p>
 * THE PREFILL MUST NOT FIRE WHEN LOADING AN EXISTING CLASS. An occurrence owns its own copies of
 * duration and capacity (prd-v2 FR-007); re-prefilling them on edit would silently replace an
 * override — and, for capacity, move the value the no-overbooking guarantee is checked against.
 * `applyTypeDefaults` is wired to the SELECT's change event, not to the form value, precisely so
 * `setValue` during load cannot trigger it.
 * </p>
 *
 * `datetime-local` carries no timezone: its value is a bare wall-clock reading. Conversion in both
 * directions goes through local-datetime.ts, because getting it backwards shifts every saved class
 * by the local offset without anything failing.
 *
 * Server failures land on the CONTROL they belong to, following the register screen: time_conflict
 * on the start field, instructor_not_trainer on the trainer field. A banner would make the admin
 * hunt for which field to change.
 */
@Component({
  imports: [ReactiveFormsModule, RouterLink],
  selector: 'app-class-form',
  styleUrl: './class-form.scss',
  templateUrl: './class-form.html',
})
export class ClassForm implements OnInit {
  private readonly classes = inject(ClassService);
  private readonly classTypes = inject(ClassTypeService);
  private readonly members = inject(MemberAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    classTypeId: ['', [Validators.required]],
    startsAt: ['', [Validators.required]],
    durationMinutes: [
      60,
      [Validators.required, Validators.min(MIN_DURATION), Validators.max(MAX_DURATION)],
    ],
    instructorUserId: ['', [Validators.required]],
    capacity: [
      12,
      [Validators.required, Validators.min(MIN_CAPACITY), Validators.max(MAX_CAPACITY)],
    ],
  });

  /** Null when creating; the class id when editing. Drives the title, the verb and the endpoint. */
  protected readonly editingId = signal<string | null>(null);

  /**
   * What the type select offers.
   *
   * Active types only when creating — a retired type must not be attachable to anything new
   * (FR-006). When editing, this holds exactly the class's own type, active or not: the select is
   * disabled anyway, and the type still has to render a label.
   */
  protected readonly classTypeOptions = signal<ClassTypeSummary[]>([]);

  protected readonly trainers = signal<TrainerSummary[]>([]);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly submitting = signal(false);

  /** A form-level message, for failures that belong to no single control. */
  protected readonly error = signal<string | null>(null);

  /**
   * Nothing to pick from. After the schedule wipe this is the FIRST screen an admin reaches, so a
   * form with two empty selects and no explanation is the worst possible first impression — the
   * template replaces the form with a signpost instead.
   */
  protected readonly noClassTypes = signal(false);
  protected readonly noTrainers = signal(false);

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    this.editingId.set(id);

    this.loading.set(true);

    try {
      // In parallel: neither depends on the other, and the form needs both before it can render.
      const [types, trainers] = await Promise.all([
        this.classTypes.getAll(),
        this.members.getTrainers(),
      ]);

      this.trainers.set(trainers);

      if (id) {
        await this.loadExisting(id, types);
      } else {
        const active = types.filter((type) => type.isActive);
        this.classTypeOptions.set(active);

        // BOTH empty states are CREATE-ONLY preconditions. An existing class already has a type and
        // an instructor; if the club later retires every type or revokes every Trainer role, the
        // admin must still be able to open that class and fix its time or capacity. Setting either
        // signal on the edit path replaces the whole form (see class-form.html) and locks them out
        // of a class that is perfectly valid.
        this.noClassTypes.set(active.length === 0);
        this.noTrainers.set(trainers.length === 0);
      }
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Populates the form from an existing class.
   *
   * The type select is narrowed to the class's OWN type and disabled: the type is immutable once an
   * occurrence exists (the API refuses a change with `class_type_immutable`), and offering
   * alternatives the server will reject is worse than offering none.
   */
  private async loadExisting(id: string, types: ClassTypeSummary[]): Promise<void> {
    const existing = await this.classes.getById(id);
    const ownType = types.find((type) => type.id === existing.classTypeId);

    // The type is guaranteed to exist — deactivated, possibly, but never deleted (FR-006). The
    // fallback covers only a type the list somehow did not return, so the select still has a label.
    this.classTypeOptions.set(
      ownType
        ? [ownType]
        : [
            {
              id: existing.classTypeId,
              name: existing.name,
              description: existing.description,
              defaultDurationMinutes: existing.durationMinutes,
              defaultCapacity: existing.capacity,
              isActive: true,
              createdAt: '',
            },
          ],
    );

    // The stored instructor may no longer be offered: /api/admin/trainers returns ACTIVE trainers
    // only, so a trainer since blocked or de-roled is absent from the list. Without an option to
    // match, the browser renders the select BLANK while the control quietly keeps its value - so a
    // required field looks unfilled, passes validation, and the admin only learns something is wrong
    // after submitting, as `unknown_instructor`.
    //
    // Class.cs documents the stale reference itself as an accepted risk of this slice; what is not
    // acceptable is hiding it until save. Same fallback trick as the class type above, flagged in the
    // label so the admin sees they must pick someone else.
    if (!this.trainers().some((trainer) => trainer.id === existing.instructorUserId)) {
      this.trainers.update((trainers) => [
        { id: existing.instructorUserId, displayName: `${existing.instructor} (nieaktywny)` },
        ...trainers,
      ]);
    }

    // setValue, NOT applyTypeDefaults: these numbers are the OCCURRENCE's, and re-deriving them from
    // the type is exactly the bug this component is shaped to prevent.
    this.form.setValue({
      classTypeId: existing.classTypeId,
      // UTC instant -> the local wall clock the input displays.
      startsAt: toLocalInputValue(existing.startsAt),
      durationMinutes: existing.durationMinutes,
      instructorUserId: existing.instructorUserId,
      capacity: existing.capacity,
    });

    this.form.controls.classTypeId.disable();
  }

  /**
   * Copies the chosen type's defaults onto the two numbers (prd-v2 FR-008).
   *
   * Called from the select's (change) event and only while creating. The admin may then override
   * either — the numbers vary legitimately per session, which is why they are copies rather than
   * references.
   */
  protected applyTypeDefaults(classTypeId: string): void {
    if (this.editingId()) {
      return;
    }

    const type = this.classTypeOptions().find((option) => option.id === classTypeId);
    if (!type) {
      return;
    }

    this.form.patchValue({
      durationMinutes: type.defaultDurationMinutes,
      capacity: type.defaultCapacity,
    });
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      // Reveal the errors rather than silently doing nothing.
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.submitting.set(true);

    // getRawValue, not value: the type control is DISABLED when editing, and `value` omits disabled
    // controls. The API requires classTypeId on an edit too — it is how it detects an attempted
    // change — so dropping it here would turn every edit into a missing_field.
    const value = this.form.getRawValue();
    const request = {
      classTypeId: value.classTypeId,
      // The local wall clock the admin typed -> the UTC instant the API stores.
      startsAt: fromLocalInputValue(value.startsAt),
      durationMinutes: value.durationMinutes,
      instructorUserId: value.instructorUserId,
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
   *
   * The CONTROL mapping is form-specific and stays here; the WORDS come from classFailureMessage, so
   * this form and the calendar's create overlay cannot describe the same refusal differently.
   */
  private applyFailure(failure: unknown): void {
    const reason = ((failure as HttpErrorResponse)?.error as ClassFailure | undefined)?.reason;

    switch (reason) {
      case 'time_conflict':
        this.reject(this.form.controls.startsAt, { timeConflict: true });
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
      case 'unknown_class_type':
      case 'inactive_class_type':
      case 'class_type_immutable':
        // The control is disabled while editing, so setErrors alone would not show anything — the
        // banner carries these. They all mean the same thing to the admin: this type cannot be used
        // for this class, reload and start again.
        this.error.set(classFailureMessage(reason));
        return;
      case 'unknown_instructor':
      case 'instructor_not_trainer':
        this.reject(this.form.controls.instructorUserId, { notATrainer: true });
        return;
      case 'missing_field':
        this.form.markAllAsTouched();
        this.error.set(classFailureMessage(reason));
        return;
      default:
        this.error.set(classFailureMessage(reason));
    }
  }

  private reject(control: AbstractControl, errors: ValidationErrors): void {
    control.setErrors(errors);
    // Required: the template only reveals errors on touched controls.
    control.markAsTouched();
  }
}
