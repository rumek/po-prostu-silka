import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ExerciseService } from '../../../core/training/exercise.service';
import {
  EXERCISE_BOUNDS,
  ExerciseFailure,
  ExerciseSummary,
} from '../../../core/training/exercise.models';
import { exerciseFailureMessage } from '../../../core/training/exercise-failure';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { createFormState } from '../../../shared/forms/form-state';
import { isVideoId, watchUrl } from '../../../core/training/youtube';
import { Field } from '../../../shared/forms/field/field';
import { Loading } from '../../../shared/forms/loading/loading';

/**
 * Every bound below matches a HasMaxLength in ExerciseConfiguration AND the server's check in
 * ExerciseEndpoints.Validate. Keep all three in step — a client bound that is looser than the
 * column turns ordinary typing into a 500.
 */
const {
  maxName: MAX_NAME,
  maxDescription: MAX_DESCRIPTION,
  maxMuscleGroup: MAX_MUSCLE_GROUP,
  maxDifficulty: MAX_DIFFICULTY,
  maxEquipment: MAX_EQUIPMENT,
  maxPreparation: MAX_PREPARATION,
  maxStartingPosition: MAX_STARTING_POSITION,
  maxExecution: MAX_EXECUTION,
  maxVideoUrl: MAX_VIDEO_URL,
} = EXERCISE_BOUNDS;

/**
 * Create and edit an exercise (prd.md FR-018, FR-019), in one component distinguished by the route
 * parameter — the same shape as ClassTypeForm.
 *
 * Server failures land on the CONTROL they belong to, following every other form here: name_taken on
 * the name field, each length refusal on its own field, invalid_video_url on the video field. A
 * banner would make the admin hunt through nine fields for the one to change.
 *
 * MUSCLE GROUP AND DIFFICULTY ARE FREE TEXT WITH SUGGESTIONS. The datalist is built from values
 * already in the library, fetched through the same unfiltered getAll() the list screen uses — so
 * there is no second endpoint to keep in step, and no controlled vocabulary to maintain. A failed
 * fetch leaves the suggestions empty and never blocks the form: they are a nudge against name drift,
 * not a constraint.
 *
 * There is no activation control here. Activation has its own endpoints and lives on the list, so a
 * careless edit cannot resurrect an exercise the admin retired.
 */
@Component({
  imports: [Loading, Field, ReactiveFormsModule, RouterLink],
  selector: 'app-exercise-form',
  styleUrl: './exercise-form.scss',
  templateUrl: './exercise-form.html',
})
export class ExerciseForm implements OnInit {
  private readonly exercises = inject(ExerciseService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly maxName = MAX_NAME;
  protected readonly maxDescription = MAX_DESCRIPTION;
  protected readonly maxMuscleGroup = MAX_MUSCLE_GROUP;
  protected readonly maxDifficulty = MAX_DIFFICULTY;
  protected readonly maxEquipment = MAX_EQUIPMENT;
  protected readonly maxPreparation = MAX_PREPARATION;
  protected readonly maxStartingPosition = MAX_STARTING_POSITION;
  protected readonly maxExecution = MAX_EXECUTION;
  protected readonly maxVideoUrl = MAX_VIDEO_URL;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(MAX_NAME)]],
    // Everything below is optional by design (FR-018) — only the ceilings are enforced.
    description: ['', [Validators.maxLength(MAX_DESCRIPTION)]],
    muscleGroup: ['', [Validators.maxLength(MAX_MUSCLE_GROUP)]],
    difficulty: ['', [Validators.maxLength(MAX_DIFFICULTY)]],
    equipment: ['', [Validators.maxLength(MAX_EQUIPMENT)]],
    preparation: ['', [Validators.maxLength(MAX_PREPARATION)]],
    startingPosition: ['', [Validators.maxLength(MAX_STARTING_POSITION)]],
    execution: ['', [Validators.maxLength(MAX_EXECUTION)]],
    // No pattern validator: the server owns what a YouTube link is, and a second definition here
    // would eventually refuse a shape the server accepts. The length ceiling is not a second
    // definition - it only mirrors the server's own guard against an absurd paste.
    videoUrl: ['', [Validators.maxLength(MAX_VIDEO_URL)]],
  });

  /** Null when creating; the exercise id when editing. Drives the title, the verb and the endpoint. */
  protected readonly editingId = signal<string | null>(null);

  protected readonly state = createFormState();

  /** The table, exposed so a field says the same thing whoever caught the rule. */
  protected readonly failureMessage = exerciseFailureMessage;

  /** Distinct values already used in the library, offered as datalist options. */
  protected readonly muscleGroups = signal<string[]>([]);
  protected readonly difficulties = signal<string[]>([]);

  async ngOnInit(): Promise<void> {
    // Deliberately not awaited before the form is usable: suggestions are a convenience, so a slow
    // or failed library fetch must not delay typing.
    void this.loadSuggestions();

    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }

    this.editingId.set(id);
    this.state.loading.set(true);

    try {
      const existing = await this.exercises.getById(id);

      this.form.setValue({
        name: existing.name,
        // The API's "absent" is null; the form's is an empty string.
        description: existing.description ?? '',
        muscleGroup: existing.muscleGroup ?? '',
        difficulty: existing.difficulty ?? '',
        equipment: existing.equipment ?? '',
        preparation: existing.preparation ?? '',
        startingPosition: existing.startingPosition ?? '',
        execution: existing.execution ?? '',
        // The stored form is a bare id; show the canonical watch URL so the field reads like a link
        // rather than like a code. Saving it back unchanged round-trips to the same id.
        videoUrl: isVideoId(existing.videoId) ? watchUrl(existing.videoId) : '',
      });
    } catch {
      this.state.loadFailed.set(true);
    } finally {
      this.state.loading.set(false);
    }
  }

  private async loadSuggestions(): Promise<void> {
    try {
      const rows = await this.exercises.getAll();

      this.muscleGroups.set(distinct(rows, (e) => e.muscleGroup));
      this.difficulties.set(distinct(rows, (e) => e.difficulty));
    } catch {
      // Silent on purpose: an empty datalist is a form without suggestions, not a broken form.
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

    const request = {
      name: value.name,
      // Collapse blank back to the API's single representation of "absent".
      description: orNull(value.description),
      muscleGroup: orNull(value.muscleGroup),
      difficulty: orNull(value.difficulty),
      equipment: orNull(value.equipment),
      preparation: orNull(value.preparation),
      startingPosition: orNull(value.startingPosition),
      execution: orNull(value.execution),
      videoUrl: orNull(value.videoUrl),
    };

    try {
      const id = this.editingId();
      if (id) {
        await this.exercises.update(id, request);
      } else {
        await this.exercises.create(request);
      }

      await this.router.navigate(['/admin/exercises']);
    } catch (failure) {
      this.applyFailure(failure);
    } finally {
      this.state.submitting.set(false);
    }
  }

  /**
   * Maps a server refusal onto the control responsible for it, so the admin sees what to change.
   * Follows class-type-form.ts's applyFailure/reject pair.
   */
  private applyFailure(failure: unknown): void {
    const info = classifyFailure(failure);

    // A 429, a 500 or a dead network belongs to no control.
    const transport = transportMessage(info);
    if (transport !== null) {
      this.state.error.set(transport);
      return;
    }

    const reason = info.reason as ExerciseFailure['reason'] | undefined;
    const message = exerciseFailureMessage(reason);

    switch (reason) {
      case 'name_taken':
      case 'name_too_long':
        this.state.reject(this.form.controls.name, { server: message });
        return;
      case 'description_too_long':
        this.state.reject(this.form.controls.description, { server: message });
        return;
      case 'muscle_group_too_long':
        this.state.reject(this.form.controls.muscleGroup, { server: message });
        return;
      case 'difficulty_too_long':
        this.state.reject(this.form.controls.difficulty, { server: message });
        return;
      case 'equipment_too_long':
        this.state.reject(this.form.controls.equipment, { server: message });
        return;
      case 'preparation_too_long':
        this.state.reject(this.form.controls.preparation, { server: message });
        return;
      case 'starting_position_too_long':
        this.state.reject(this.form.controls.startingPosition, { server: message });
        return;
      case 'execution_too_long':
        this.state.reject(this.form.controls.execution, { server: message });
        return;
      case 'invalid_video_url':
        this.state.reject(this.form.controls.videoUrl, { server: message });
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

/** Blank is the form's "absent"; null is the API's. */
function orNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

/** The distinct non-null values of one field across the library, alphabetically. */
function distinct(
  rows: ExerciseSummary[],
  pick: (row: ExerciseSummary) => string | null,
): string[] {
  const values = rows.map(pick).filter((value): value is string => !!value);

  return [...new Set(values)].sort((a, b) => a.localeCompare(b, 'pl'));
}
