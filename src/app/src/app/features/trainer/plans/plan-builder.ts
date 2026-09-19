import {
  CdkDrag,
  CdkDragDrop,
  CdkDragHandle,
  CdkDropList,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import {
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ExerciseService } from '../../../core/training/exercise.service';
import { ExerciseSummary } from '../../../core/training/exercise.models';
import { TrainingPlanService } from '../../../core/training/training-plan.service';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { trainingPlanFailureMessage } from '../../../core/training/training-plan-failure';
import { createFormState } from '../../../shared/forms/form-state';
import {
  AssignableMember,
  TRAINING_PLAN_BOUNDS,
  TrainingPlanFailure,
  TrainingPlanItemRequest,
} from '../../../core/training/training-plan.models';

/**
 * Every bound below matches a constant in TrainingPlanEndpoints, which in turn matches a column in
 * TrainingPlanConfiguration / TrainingPlanItemConfiguration. Keep all three in step — a client bound
 * looser than the server's turns ordinary typing into an unexplained 400.
 */
// Fourteen of this union's seventeen refusal sentences quote one of these, and those sentences
// now live in `training-plan-failure.ts` — so the numbers moved beside the union (S-19). A message
// that quotes a bound the form owns privately drifts the first time the bound moves.
const {
  maxName: MAX_NAME,
  maxReps: MAX_REPS,
  maxNote: MAX_NOTE,
  maxItems: MAX_ITEMS,
  minSets: MIN_SETS,
  maxSets: MAX_SETS,
  minRest: MIN_REST,
  maxRest: MAX_REST,
  minDuration: MIN_DURATION,
  maxDuration: MAX_DURATION,
  minWeight: MIN_WEIGHT,
  maxWeight: MAX_WEIGHT,
} = TRAINING_PLAN_BOUNDS;

/** One item row's form shape, named so the FormArray's element type is not written out four times. */
type ItemGroup = FormGroup<{
  exerciseId: FormControl<string>;
  /** Disabled — the label the row renders, carried inside the group so reordering moves it along. */
  exerciseName: FormControl<string>;
  sets: FormControl<number | null>;
  reps: FormControl<string>;
  weightKg: FormControl<number | null>;
  restSeconds: FormControl<number | null>;
  durationSeconds: FormControl<number | null>;
  note: FormControl<string>;
}>;

/**
 * Create and edit a training plan (prd.md FR-015, FR-016), in one component distinguished by the
 * route parameter — the same shape as ExerciseForm and ClassTypeForm.
 *
 * THE ARRAY ORDER IS THE PLAN'S ORDER. No control holds a position and nothing renumbers: dragging
 * moves an element in the FormArray, and the server numbers what it receives. This is why reordering
 * cannot produce a duplicate or a gap — there is no number to get wrong.
 *
 * KNOWN AND ACCEPTED GAP: reordering is pointer-only. `@angular/cdk/drag-drop` ships no keyboard
 * path, and building one was weighed against the slice and deliberately deferred (plan.md, Phase 3).
 * Everything else here — adding, removing, editing every parameter, saving — works from the keyboard
 * alone, and a plan saved in any order is still a valid plan.
 *
 * THE MEMBER CANNOT BE CHANGED WHILE EDITING. A plan does not move between people; it is superseded.
 * The server refuses a mismatched memberId with `member_changed` rather than ignoring it, and the
 * control is disabled here so that refusal is something only a stale tab can trigger.
 */
@Component({
  imports: [CdkDrag, CdkDragHandle, CdkDropList, ReactiveFormsModule, RouterLink],
  selector: 'app-plan-builder',
  styleUrl: './plan-builder.scss',
  templateUrl: './plan-builder.html',
})
export class PlanBuilder implements OnInit {
  private readonly plans = inject(TrainingPlanService);
  private readonly exercises = inject(ExerciseService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);

  protected readonly maxName = MAX_NAME;
  protected readonly maxReps = MAX_REPS;
  protected readonly maxNote = MAX_NOTE;
  protected readonly maxItems = MAX_ITEMS;
  protected readonly minSets = MIN_SETS;
  protected readonly maxSets = MAX_SETS;
  protected readonly minRest = MIN_REST;
  protected readonly maxRest = MAX_REST;
  protected readonly minDuration = MIN_DURATION;
  protected readonly maxDuration = MAX_DURATION;
  protected readonly minWeight = MIN_WEIGHT;
  protected readonly maxWeight = MAX_WEIGHT;

  protected readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(MAX_NAME)]],
    memberId: ['', [Validators.required]],
  });

  /**
   * The items, outside the group above rather than inside it, so the template can iterate
   * `items.controls` without an `$any` cast and the drop handler can reorder it directly.
   */
  protected readonly items = this.fb.array<ItemGroup>([]);

  /** Null when creating; the plan id when editing. Drives the title, the verb and the endpoint. */
  protected readonly editingId = signal<string | null>(null);

  protected readonly state = createFormState();

  /** The table, exposed to the template so client and server messages cannot disagree. */
  protected readonly failureMessage = trainingPlanFailureMessage;

  /** Who the plan may be assigned to. Empty until the picker's fetch resolves. */
  protected readonly members = signal<AssignableMember[]>([]);
  protected readonly membersFailed = signal(false);

  /**
   * Whose plan this is, on the EDIT path only, taken from the loaded plan rather than from
   * `members()`. The picker lists active accounts; a member blocked after assignment keeps their
   * plan, so looking their name up in that list would find nothing.
   */
  protected readonly memberName = signal('');

  /** The exercise library, ACTIVE ONLY — a retired exercise must not be prescribed anew. */
  protected readonly library = signal<ExerciseSummary[]>([]);
  protected readonly libraryFailed = signal(false);

  protected readonly exerciseSearch = signal('');

  /**
   * The library minus what is already in the plan, filtered by the search box.
   *
   * Excluding chosen exercises rather than letting a second click add a duplicate: the server
   * refuses `duplicate_exercise`, and a picker that offers a choice the save will reject is a worse
   * explanation than one that simply stops offering it.
   */
  protected readonly pickable = computed(() => {
    const term = this.exerciseSearch().trim().toLocaleLowerCase('pl');
    const chosen = new Set(this.chosenIds());

    return this.library().filter(
      (row) =>
        !chosen.has(row.id) &&
        (term.length === 0 || row.name.toLocaleLowerCase('pl').includes(term)),
    );
  });

  /**
   * Exercise ids already in the plan, as a signal so `pickable` recomputes when a row is added or
   * removed. A FormArray is not reactive to signals, so this is updated alongside every mutation —
   * the one place in this component where two things must be kept in step, and it is deliberate:
   * the alternative is re-deriving from `items.controls` inside a computed, which would never fire.
   */
  private readonly chosenIds = signal<readonly string[]>([]);

  protected readonly atItemLimit = computed(() => this.chosenIds().length >= MAX_ITEMS);

  async ngOnInit(): Promise<void> {
    // Not awaited before the form is usable: the pickers are data the trainer types alongside, and a
    // slow library fetch must not delay naming the plan.
    void this.loadMembers();
    void this.loadLibrary();

    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }

    this.editingId.set(id);
    this.state.loading.set(true);

    try {
      const existing = await this.plans.getById(id);

      this.form.setValue({ name: existing.name, memberId: existing.memberId });

      // The member is fixed for the life of a plan; see the class docblock.
      this.form.controls.memberId.disable();
      this.memberName.set(existing.memberDisplayName);

      // Already ordered by the API. Nothing here re-sorts, and nothing reads `position`.
      for (const item of existing.items) {
        this.items.push(
          this.buildItemGroup(item.exerciseId, item.exerciseName, {
            sets: item.sets,
            reps: item.reps ?? '',
            weightKg: item.weightKg,
            restSeconds: item.restSeconds,
            durationSeconds: item.durationSeconds,
            note: item.note ?? '',
          }),
        );
      }

      this.syncChosenIds();
    } catch {
      this.state.loadFailed.set(true);
    } finally {
      this.state.loading.set(false);
    }
  }

  private async loadMembers(): Promise<void> {
    try {
      this.members.set(await this.plans.getAssignableMembers());
      this.membersFailed.set(false);
    } catch {
      // Surfaced, unlike ExerciseForm's silent datalist: without a member there is no plan to save,
      // so an empty picker is a broken form rather than a form without suggestions.
      this.membersFailed.set(true);
    }
  }

  private async loadLibrary(): Promise<void> {
    try {
      const rows = await this.exercises.getAll();

      // Filtered here rather than by the API: the endpoint serves the admin's list screen, which
      // needs the retired ones to offer reactivation. A plan must not prescribe them — the server
      // refuses `inactive_exercise` — so they are dropped before the picker ever shows them.
      this.library.set(rows.filter((row) => row.isActive));
      this.libraryFailed.set(false);
    } catch {
      this.libraryFailed.set(true);
    }
  }

  protected onExerciseSearch(value: string): void {
    this.exerciseSearch.set(value);
  }

  protected addExercise(exercise: ExerciseSummary): void {
    if (this.atItemLimit()) {
      return;
    }

    this.items.push(this.buildItemGroup(exercise.id, exercise.name));
    this.syncChosenIds();

    // The search matched something that is now in the plan; leaving the term would show a list the
    // trainer has just emptied.
    this.exerciseSearch.set('');
  }

  protected removeItem(index: number): void {
    this.items.removeAt(index);
    this.syncChosenIds();
  }

  /**
   * The only mutation reordering performs. `moveItemInArray` works on a plain array, so the controls
   * are moved out, reordered and put back — a FormArray has no move operation of its own.
   */
  protected onDrop(event: CdkDragDrop<unknown>): void {
    if (event.previousIndex === event.currentIndex) {
      return;
    }

    const controls = [...this.items.controls];
    moveItemInArray(controls, event.previousIndex, event.currentIndex);

    this.items.clear({ emitEvent: false });
    for (const control of controls) {
      this.items.push(control, { emitEvent: false });
    }

    this.items.markAsDirty();
  }

  protected itemName(index: number): string {
    return this.items.at(index).getRawValue().exerciseName;
  }

  protected async submit(): Promise<void> {
    if (this.items.length === 0) {
      this.state.error.set('Dodaj przynajmniej jedno ćwiczenie do planu.');
      return;
    }

    if (this.form.invalid || this.items.invalid) {
      // Reveal the errors rather than silently doing nothing.
      this.form.markAllAsTouched();
      this.items.markAllAsTouched();
      return;
    }

    this.state.error.set(null);
    this.state.submitting.set(true);

    // getRawValue, not value: the member control is DISABLED while editing, and `value` omits
    // disabled controls. The server validates memberId on edit rather than ignoring it, so
    // sending an empty one would be refused with `member_changed`.
    const header = this.form.getRawValue();

    const request = {
      name: header.name.trim(),
      memberId: header.memberId,
      items: this.items.controls.map((control) => toItemRequest(control.getRawValue())),
    };

    try {
      const id = this.editingId();
      if (id) {
        await this.plans.update(id, request);
      } else {
        await this.plans.create(request);
      }

      await this.router.navigate(['/trainer/plans']);
    } catch (failure) {
      this.applyFailure(failure);
    } finally {
      this.state.submitting.set(false);
    }
  }

  /**
   * Maps a server refusal onto the control responsible for it, following ExerciseForm's
   * applyFailure/reject pair.
   *
   * PER-ITEM REASONS CARRY NO INDEX. The API refuses on the first offending item without saying
   * which, so those land on the banner naming the field rather than on a row. Every one of them is
   * already blocked by a client validator, so reaching this branch means the two definitions have
   * drifted — the message says what to look for rather than pretending to point at a control.
   */
  private applyFailure(failure: unknown): void {
    const info = classifyFailure(failure);

    // A 429, a 500 or a dead network is not a plan rule — telling the trainer to shorten a note
    // over one would send them hunting through seventeen rows for nothing.
    const transport = transportMessage(info);
    if (transport !== null) {
      this.state.error.set(transport);
      return;
    }

    const reason = info.reason as TrainingPlanFailure['reason'] | undefined;
    const message = trainingPlanFailureMessage(reason);

    switch (reason) {
      case 'name_too_long':
        this.state.reject(this.form.controls.name, { server: message });
        return;

      // Touches the form so the required-field markers show, THEN banners the rule — the refusal
      // names two controls at once, so neither of them owns it.
      case 'missing_field':
        this.form.markAllAsTouched();
        this.state.error.set(message);
        return;

      // Both mean the chosen member cannot hold this plan; the control is named as well as the
      // banner, because picking somebody else is the whole of the fix.
      case 'member_not_found':
      case 'member_not_active':
        this.state.reject(this.form.controls.memberId, { server: message });
        this.state.error.set(message);
        return;

      default:
        this.state.error.set(message);
    }
  }

  /**
   * Builds one item row.
   *
   * `exerciseName` is carried as a disabled control rather than in a parallel array: it is what the
   * row renders, and keeping it inside the group means reordering moves the label with its numbers
   * for free. Disabled so `getRawValue()` still returns it while nothing can edit it.
   */
  private buildItemGroup(
    exerciseId: string,
    exerciseName: string,
    values?: {
      sets: number | null;
      reps: string;
      weightKg: number | null;
      restSeconds: number | null;
      durationSeconds: number | null;
      note: string;
    },
  ): ItemGroup {
    return this.fb.group({
      exerciseId: this.fb.nonNullable.control(exerciseId, [Validators.required]),
      exerciseName: this.fb.nonNullable.control({ value: exerciseName, disabled: true }),
      // Every parameter is optional (FR-015): a trainer may prescribe a bare exercise. The
      // validators bound the value only when there IS one, which `min`/`max` do for null already.
      sets: this.fb.control<number | null>(values?.sets ?? null, [
        Validators.min(MIN_SETS),
        Validators.max(MAX_SETS),
      ]),
      reps: this.fb.nonNullable.control(values?.reps ?? '', [Validators.maxLength(MAX_REPS)]),
      weightKg: this.fb.control<number | null>(values?.weightKg ?? null, [
        Validators.min(MIN_WEIGHT),
        Validators.max(MAX_WEIGHT),
      ]),
      restSeconds: this.fb.control<number | null>(values?.restSeconds ?? null, [
        Validators.min(MIN_REST),
        Validators.max(MAX_REST),
      ]),
      durationSeconds: this.fb.control<number | null>(values?.durationSeconds ?? null, [
        Validators.min(MIN_DURATION),
        Validators.max(MAX_DURATION),
      ]),
      note: this.fb.nonNullable.control(values?.note ?? '', [Validators.maxLength(MAX_NOTE)]),
    }) as ItemGroup;
  }

  private syncChosenIds(): void {
    this.chosenIds.set(this.items.controls.map((control) => control.getRawValue().exerciseId));
  }
}

/** Blank and NaN are the form's "absent"; null is the API's. */
function toItemRequest(value: {
  exerciseId: string;
  sets: number | null;
  reps: string;
  weightKg: number | null;
  restSeconds: number | null;
  durationSeconds: number | null;
  note: string;
}): TrainingPlanItemRequest {
  return {
    exerciseId: value.exerciseId,
    sets: numberOrNull(value.sets),
    reps: textOrNull(value.reps),
    weightKg: numberOrNull(value.weightKg),
    restSeconds: numberOrNull(value.restSeconds),
    durationSeconds: numberOrNull(value.durationSeconds),
    note: textOrNull(value.note),
  };
}

function textOrNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

/**
 * A cleared `<input type="number">` reports an empty string, which Angular hands over as null — but a
 * half-typed one ("-", "1e") reports NaN, and JSON.stringify turns NaN into `null` anyway. Collapsing
 * both here keeps that accident explicit rather than relying on the serialiser.
 */
function numberOrNull(value: number | null): number | null {
  return value === null || Number.isNaN(value) ? null : value;
}
