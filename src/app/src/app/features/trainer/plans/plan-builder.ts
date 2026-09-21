import {
  CdkDrag,
  CdkDragDrop,
  CdkDragHandle,
  CdkDropList,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ExerciseService } from '../../../core/training/exercise.service';
import { ExerciseSummary } from '../../../core/training/exercise.models';
import { TrainingPlanService } from '../../../core/training/training-plan.service';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { trainingPlanFailureMessage } from '../../../core/training/training-plan-failure';
import { createFormState } from '../../../shared/forms/form-state';
import { createLoadFence } from '../../../shared/forms/load-fence';
import { ToastService } from '../../../shared/toast/toast.service';
import { Field } from '../../../shared/forms/field/field';
import { Loading } from '../../../shared/forms/loading/loading';
import { Empty } from '../../../shared/forms/empty/empty';
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
 * One member's training plan (prd.md FR-015, FR-016), reached THROUGH THE MEMBER (S-22, UX-07/UX-08):
 * `/admin/members/:id/plan` for an admin and `/trainer/members/:id/plan` for a trainer. One builder,
 * mounted twice; the mounts differ only by the route data naming the list to return to, which keeps
 * "the URL says who can use it" (S-11) true.
 *
 * CREATE OR EDIT IS DECIDED BY THE LOAD, not by the URL. A member with no plan gets an empty builder
 * whose submit creates one; a member with a plan gets that plan, editable. After a create the screen
 * switches to editing the plan it just made, so a second save edits rather than creating yet another
 * plan and archiving the first.
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
 * THE MEMBER IS FIXED BY THE URL. There is no picker: the member id comes from the route and is sent
 * in the body of both create and edit. A plan does not move between people, and on edit the server's
 * `member_changed` refusal is what catches a body that disagrees with the plan it names.
 */
@Component({
  imports: [
    Empty,
    Loading,
    Field,
    CdkDrag,
    CdkDragHandle,
    CdkDropList,
    ReactiveFormsModule,
    RouterLink,
  ],
  selector: 'app-plan-builder',
  styleUrl: './plan-builder.scss',
  templateUrl: './plan-builder.html',
})
export class PlanBuilder implements OnInit {
  private readonly plans = inject(TrainingPlanService);
  private readonly exercises = inject(ExerciseService);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);
  private readonly toast = inject(ToastService);
  private readonly fence = createLoadFence();

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
  });

  /**
   * The items, outside the group above rather than inside it, so the template can iterate
   * `items.controls` without an `$any` cast and the drop handler can reorder it directly.
   */
  protected readonly items = this.fb.array<ItemGroup>([]);

  /** Null while the member has no plan; the plan id once they do. Drives the verb and the endpoint. */
  protected readonly editingId = signal<string | null>(null);

  protected readonly state = createFormState();

  /**
   * Outlet 4 for a member that does not exist — a screen that could not be populated at all, in the
   * transport table's words. Kept apart from `loadFailed` because retrying cannot fix it.
   */
  protected readonly notFound = signal<string | null>(null);

  /** The table, exposed to the template so client and server messages cannot disagree. */
  protected readonly failureMessage = trainingPlanFailureMessage;

  /**
   * The member id from the URL — whose plan this is, for the load and for every save. A signal fed by
   * `paramMap`, not a snapshot: the router REUSES this component when only `:id` changes, and a
   * snapshot would then show one member's plan under another's URL and save it with the wrong id —
   * which `member_changed` cannot catch, since the body and the stored plan would still agree.
   */
  private readonly memberId = signal('');

  /**
   * Whose plan this is, from the load. Any status: an admin opens a BLOCKED member's plan too, so
   * this is never looked up in a list of active members.
   */
  protected readonly member = signal<AssignableMember | null>(null);

  /** Where "back" goes — the list this mount was reached from, named by the route data. */
  protected readonly membersLink: string =
    (this.route.snapshot.data['membersLink'] as string | undefined) ?? '/';

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

  constructor() {
    // The ONLY thing that loads the member's plan, as the query-param subscription is on the trainer's
    // list: a new id is a new member, and everything on screen belongs to the old one.
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const id = params.get('id') ?? '';
      if (id === this.memberId()) {
        return;
      }

      this.memberId.set(id);
      void this.load();
    });
  }

  ngOnInit(): void {
    // Not awaited before the form is usable: the library is data the trainer types alongside, and a
    // slow fetch must not delay naming the plan.
    void this.loadLibrary();
  }

  /** The member and their plan. Also the load-failure state's retry. */
  protected async load(): Promise<void> {
    const generation = this.fence.begin();

    this.state.loading.set(true);
    this.state.loadFailed.set(false);
    this.notFound.set(null);

    try {
      const { member, plan } = await this.plans.getMemberPlan(this.memberId());

      if (!this.fence.isCurrent(generation)) {
        return;
      }

      this.member.set(member);
      this.items.clear();
      this.state.error.set(null);

      if (!plan) {
        // Reset rather than assumed: after a member change the previous member's plan is still here.
        this.editingId.set(null);
        this.form.reset();
      } else {
        this.editingId.set(plan.id);
        this.form.setValue({ name: plan.name });

        // Already ordered by the API. Nothing here re-sorts, and nothing reads `position`.
        for (const item of plan.items) {
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
      }

      this.syncChosenIds();
    } catch (failure) {
      if (!this.fence.isCurrent(generation)) {
        return;
      }

      const info = classifyFailure(failure);
      if (info.kind === 'notFound') {
        this.notFound.set(transportMessage(info));
      } else {
        this.state.loadFailed.set(true);
      }
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.state.loading.set(false);
      }
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

    // The member comes from the URL on both paths. On edit the server compares it with the stored
    // plan and refuses a mismatch with `member_changed` — the URL/body agreement check.
    const request = {
      name: this.form.getRawValue().name.trim(),
      memberId: this.memberId(),
      items: this.items.controls.map((control) => toItemRequest(control.getRawValue())),
    };

    try {
      const id = this.editingId();
      if (id) {
        await this.plans.update(id, request);
      } else {
        // THE SCREEN BECOMES THE EDIT VIEW of what it just created. Without this a second save would
        // POST again — archiving the plan just made and creating another.
        const created = await this.plans.create(request);

        // The URL moved to another member while the POST was in flight: this plan is not theirs.
        if (request.memberId !== this.memberId()) {
          return;
        }

        this.editingId.set(created.id);
      }

      // Stay on the screen and say so (outlet 3): the member's plan is what is being looked at, and
      // the passes screen, this one's precedent, stays put after a save too.
      this.form.markAsPristine();
      this.items.markAsPristine();
      this.toast.success('Plan zapisany.');
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

      // member_not_found, member_not_active and member_changed land here, in the banner (outlet 2):
      // the member is fixed by the URL, so there is no control left to name.
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
