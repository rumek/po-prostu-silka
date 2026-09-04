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
import { ExerciseService } from '../../../core/training/exercise.service';
import { ExerciseFailure, ExerciseSummary } from '../../../core/training/exercise.models';
import { isVideoId, watchUrl } from '../../../core/training/youtube';

/**
 * Every bound below matches a HasMaxLength in ExerciseConfiguration AND the server's check in
 * ExerciseEndpoints.Validate. Keep all three in step — a client bound that is looser than the
 * column turns ordinary typing into a 500.
 */
const MAX_NAME = 200;
const MAX_DESCRIPTION = 1000;
const MAX_MUSCLE_GROUP = 100;
const MAX_DIFFICULTY = 50;
const MAX_EQUIPMENT = 200;
const MAX_PREPARATION = 2000;
const MAX_STARTING_POSITION = 2000;
const MAX_EXECUTION = 4000;

/**
 * Mirrors MaxVideoUrlLength in ExerciseEndpoints. The one bound here that guards no column - the
 * server stores the parsed id, not the pasted link - so it exists only to stop an absurd paste
 * reaching the parser.
 */
const MAX_VIDEO_URL = 2048;

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
  imports: [ReactiveFormsModule, RouterLink],
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

  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly submitting = signal(false);

  /** A form-level message, for failures that belong to no single control. */
  protected readonly error = signal<string | null>(null);

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
    this.loading.set(true);

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
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
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

    this.error.set(null);
    this.submitting.set(true);

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
      this.submitting.set(false);
    }
  }

  /**
   * Maps a server refusal onto the control responsible for it, so the admin sees what to change.
   * Follows class-type-form.ts's applyFailure/reject pair.
   */
  private applyFailure(failure: unknown): void {
    const reason = ((failure as HttpErrorResponse)?.error as ExerciseFailure | undefined)?.reason;

    switch (reason) {
      case 'name_taken':
        this.reject(this.form.controls.name, { nameTaken: true });
        return;
      case 'name_too_long':
        this.reject(this.form.controls.name, { maxlength: true });
        return;
      case 'description_too_long':
        this.reject(this.form.controls.description, { maxlength: true });
        return;
      case 'muscle_group_too_long':
        this.reject(this.form.controls.muscleGroup, { maxlength: true });
        return;
      case 'difficulty_too_long':
        this.reject(this.form.controls.difficulty, { maxlength: true });
        return;
      case 'equipment_too_long':
        this.reject(this.form.controls.equipment, { maxlength: true });
        return;
      case 'preparation_too_long':
        this.reject(this.form.controls.preparation, { maxlength: true });
        return;
      case 'starting_position_too_long':
        this.reject(this.form.controls.startingPosition, { maxlength: true });
        return;
      case 'execution_too_long':
        this.reject(this.form.controls.execution, { maxlength: true });
        return;
      case 'invalid_video_url':
        this.reject(this.form.controls.videoUrl, { invalidVideoUrl: true });
        return;
      case 'missing_field':
        this.form.markAllAsTouched();
        this.error.set('Uzupełnij nazwę ćwiczenia.');
        return;
      default:
        this.error.set('Nie udało się zapisać ćwiczenia. Spróbuj ponownie za chwilę.');
    }
  }

  private reject(control: AbstractControl, errors: ValidationErrors): void {
    control.setErrors(errors);
    // Required: the template only reveals errors on touched controls.
    control.markAsTouched();
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
