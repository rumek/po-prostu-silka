import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import { TrainerSummary } from '../../../core/admin/member-admin.models';
import { ClassService } from '../../../core/scheduling/class.service';
import { ClassGroupService } from '../../../core/scheduling/class-group.service';
import { ClassFailure } from '../../../core/scheduling/class.models';
import { ClassGroupSummary } from '../../../core/scheduling/class-group.models';
import { classFailureMessage } from '../../../core/scheduling/class-failure';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { createFormState } from '../../../shared/forms/form-state';
import { fromLocalInputValue, toLocalInputValue } from '../../../core/scheduling/local-datetime';
import { Field } from '../../../shared/forms/field/field';
import { Select } from '../../../shared/forms/select/select';
import { Loading } from '../../../shared/forms/loading/loading';
import { Empty } from '../../../shared/forms/empty/empty';
import { Icon } from '../../../shared/icons/icon';

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
 * picks a group and a trainer, and the two numbers arrive PREFILLED from the group's defaults.
 * That prefill is the whole reason the definition layer exists — it is what removes the retyping.
 * </p>
 *
 * <p>
 * THE PREFILL MUST NOT FIRE WHEN LOADING AN EXISTING CLASS. An occurrence owns its own copies of
 * duration and capacity (prd-v2 FR-007); re-prefilling them on edit would silently replace an
 * override — and, for capacity, move the value the no-overbooking guarantee is checked against.
 * `applyGroupDefaults` is wired to the SELECT's change event, not to the form value, precisely so
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
  imports: [Empty, Icon, Loading, Select, Field, ReactiveFormsModule, RouterLink],
  selector: 'app-class-form',
  styleUrl: './class-form.scss',
  templateUrl: './class-form.html',
})
export class ClassForm implements OnInit {
  private readonly classes = inject(ClassService);
  private readonly classGroups = inject(ClassGroupService);
  private readonly members = inject(MemberAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    classGroupId: ['', [Validators.required]],
    startsAt: ['', [Validators.required]],
    durationMinutes: [
      60,
      [Validators.required, Validators.min(MIN_DURATION), Validators.max(MAX_DURATION)],
    ],
    instructorMemberId: ['', [Validators.required]],
    capacity: [
      12,
      [Validators.required, Validators.min(MIN_CAPACITY), Validators.max(MAX_CAPACITY)],
    ],
  });

  /** Null when creating; the class id when editing. Drives the title, the verb and the endpoint. */
  protected readonly editingId = signal<string | null>(null);

  /**
   * What the group select offers.
   *
   * Active groups only when creating — a retired group must not be attachable to anything new
   * (FR-006). When editing, this holds exactly the class's own group, active or not: the select is
   * disabled anyway, and the group still has to render a label.
   */
  protected readonly classGroupOptions = signal<ClassGroupSummary[]>([]);

  protected readonly trainers = signal<TrainerSummary[]>([]);

  protected readonly state = createFormState();

  /** A form-level message, for failures that belong to no single control. */
  /**
   * The table, exposed to the template.
   *
   * THIS IS WHAT MAKES THE DOCBLOCK BELOW TRUE. It said the words come from `classFailureMessage`
   * and meant it only on the banner branches — the template wrote its own sentence for
   * `time_conflict` and `starts_in_past`, so one refusal read two ways depending on where it landed
   * (`class-failure.ts:34` vs `class-form.html:76-79`). Now the form decides only WHICH control.
   */
  protected readonly failureMessage = classFailureMessage;

  /**
   * Nothing to pick from. After the schedule wipe this is the FIRST screen an admin reaches, so a
   * form with two empty selects and no explanation is the worst possible first impression — the
   * template replaces the form with a signpost instead.
   */
  protected readonly noClassGroups = signal(false);
  protected readonly noTrainers = signal(false);

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    this.editingId.set(id);

    this.state.loading.set(true);

    try {
      // In parallel: neither depends on the other, and the form needs both before it can render.
      const [groups, trainers] = await Promise.all([
        this.classGroups.getAll(),
        this.members.getTrainers(),
      ]);

      this.trainers.set(trainers);

      if (id) {
        await this.loadExisting(id, groups);
      } else {
        const active = groups.filter((group) => group.isActive);
        this.classGroupOptions.set(active);

        // BOTH empty states are CREATE-ONLY preconditions. An existing class already has a group and
        // an instructor; if the club later retires every group or revokes every Trainer role, the
        // admin must still be able to open that class and fix its time or capacity. Setting either
        // signal on the edit path replaces the whole form (see class-form.html) and locks them out
        // of a class that is perfectly valid.
        this.noClassGroups.set(active.length === 0);
        this.noTrainers.set(trainers.length === 0);
      }
    } catch {
      this.state.loadFailed.set(true);
    } finally {
      this.state.loading.set(false);
    }
  }

  /**
   * Populates the form from an existing class.
   *
   * The group select is narrowed to the class's OWN group and disabled: the group is immutable once an
   * occurrence exists (the API refuses a change with `class_group_immutable`), and offering
   * alternatives the server will reject is worse than offering none.
   */
  private async loadExisting(id: string, groups: ClassGroupSummary[]): Promise<void> {
    const existing = await this.classes.getById(id);
    const ownGroup = groups.find((group) => group.id === existing.classGroupId);

    // The group is guaranteed to exist — deactivated, possibly, but never deleted (FR-006). The
    // fallback covers only a group the list somehow did not return, so the select still has a label.
    this.classGroupOptions.set(
      ownGroup
        ? [ownGroup]
        : [
            {
              id: existing.classGroupId,
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
    // acceptable is hiding it until save. Same fallback trick as the group above, flagged in the
    // label so the admin sees they must pick someone else.
    if (!this.trainers().some((trainer) => trainer.id === existing.instructorMemberId)) {
      this.trainers.update((trainers) => [
        { id: existing.instructorMemberId, displayName: `${existing.instructor} (nieaktywny)` },
        ...trainers,
      ]);
    }

    // setValue, NOT applyGroupDefaults: these numbers are the OCCURRENCE's, and re-deriving them from
    // the group is exactly the bug this component is shaped to prevent.
    this.form.setValue({
      classGroupId: existing.classGroupId,
      // UTC instant -> the local wall clock the input displays.
      startsAt: toLocalInputValue(existing.startsAt),
      durationMinutes: existing.durationMinutes,
      instructorMemberId: existing.instructorMemberId,
      capacity: existing.capacity,
    });

    this.form.controls.classGroupId.disable();
  }

  /**
   * Copies the chosen group's defaults onto the two numbers (prd-v2 FR-008).
   *
   * Called from the select's (change) event and only while creating. The admin may then override
   * either — the numbers vary legitimately per session, which is why they are copies rather than
   * references.
   */
  protected applyGroupDefaults(classGroupId: string): void {
    if (this.editingId()) {
      return;
    }

    const group = this.classGroupOptions().find((option) => option.id === classGroupId);
    if (!group) {
      return;
    }

    this.form.patchValue({
      durationMinutes: group.defaultDurationMinutes,
      capacity: group.defaultCapacity,
    });
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      // Reveal the errors rather than silently doing nothing.
      this.form.markAllAsTouched();
      return;
    }

    this.state.error.set(null);
    this.state.submitting.set(true);

    // getRawValue, not value: the group control is DISABLED when editing, and `value` omits disabled
    // controls. The API requires classGroupId on an edit too — it is how it detects an attempted
    // change — so dropping it here would turn every edit into a missing_field.
    const value = this.form.getRawValue();
    const request = {
      classGroupId: value.classGroupId,
      // The local wall clock the admin typed -> the UTC instant the API stores.
      startsAt: fromLocalInputValue(value.startsAt),
      durationMinutes: value.durationMinutes,
      instructorMemberId: value.instructorMemberId,
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
      this.state.submitting.set(false);
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
    const info = classifyFailure(failure);

    // A 429, a 500 or a dead network is not a scheduling rule and belongs to no control.
    const transport = transportMessage(info);
    if (transport !== null) {
      this.state.error.set(transport);
      return;
    }

    const reason = info.reason as ClassFailure['reason'] | undefined;

    switch (reason) {
      case 'time_conflict':
      case 'starts_in_past':
        this.state.reject(this.form.controls.startsAt, { server: classFailureMessage(reason) });
        return;
      case 'invalid_capacity':
        this.state.reject(this.form.controls.capacity, { server: classFailureMessage(reason) });
        return;
      case 'invalid_duration':
        this.state.reject(this.form.controls.durationMinutes, {
          server: classFailureMessage(reason),
        });
        return;
      case 'unknown_class_group':
      case 'inactive_class_group':
      case 'class_group_immutable':
        // The control is disabled while editing, so setErrors alone would not show anything — the
        // banner carries these. They all mean the same thing to the admin: this group cannot be used
        // for this class, reload and start again.
        this.state.error.set(classFailureMessage(reason));
        return;
      case 'unknown_instructor':
      case 'instructor_not_trainer':
        this.state.reject(this.form.controls.instructorMemberId, {
          server: classFailureMessage(reason),
        });
        return;
      case 'missing_field':
        this.form.markAllAsTouched();
        this.state.error.set(classFailureMessage(reason));
        return;
      default:
        this.state.error.set(classFailureMessage(reason));
    }
  }
}
