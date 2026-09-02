import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MemberAdminService } from '../../../core/admin/member-admin.service';
import { TrainerSummary } from '../../../core/admin/member-admin.models';
import { classFailureMessage } from '../../../core/scheduling/class-failure';
import { ClassService } from '../../../core/scheduling/class.service';
import { ClassTypeService } from '../../../core/scheduling/class-type.service';
import { ClassTypeSummary } from '../../../core/scheduling/class-type.models';
import { DrawnRange } from '../../../shared/calendar/schedule-calendar';

/**
 * Finishes a drag-to-create gesture (prd-v2 FR-019).
 *
 * The gesture fixes WHEN and HOW LONG. It cannot express which class this is or who runs it, and
 * those are exactly the two fields S-06 made non-typeable — so the overlay is two selects over the
 * same lists the class form uses, plus the two numbers, prefilled.
 *
 * <h2>The prefill rule, unchanged from the form</h2>
 *
 * Picking a type COPIES its defaults onto this occurrence; nothing is ever resolved back through the
 * type (prd-v2 FR-007). The drawn duration wins over the type's default, because the admin just
 * expressed it with the gesture — the capacity has no such gesture, so it takes the default.
 *
 * <h2>Why it does not reuse the class form</h2>
 *
 * It is a second create surface, which is a real cost: two forms drift. The words are pinned against
 * that by `classFailureMessage`, shared with `class-form`. What is NOT shared is the mapping of a
 * refusal onto a control — the two forms have different controls.
 */
@Component({
  // On the host, not on the panel: Escape has to close the overlay wherever focus is, including
  // before the admin has touched a control.
  host: { '(document:keydown.escape)': 'close()' },
  imports: [DatePipe, FormsModule],
  selector: 'app-class-create-overlay',
  styleUrl: './class-create-overlay.scss',
  templateUrl: './class-create-overlay.html',
})
export class ClassCreateOverlay implements OnInit {
  private readonly classes = inject(ClassService);
  private readonly classTypes = inject(ClassTypeService);
  private readonly members = inject(MemberAdminService);

  /** When and how long, from the gesture. */
  readonly drawn = input.required<DrawnRange>();

  readonly created = output<void>();
  readonly closed = output<void>();

  protected readonly types = signal<ClassTypeSummary[]>([]);
  protected readonly trainers = signal<TrainerSummary[]>([]);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly classTypeId = signal('');
  protected readonly instructorUserId = signal('');
  protected readonly durationMinutes = signal(0);
  protected readonly capacity = signal(0);

  /**
   * Both empty states block a submit that cannot succeed. Same rule as the class form's `noClassTypes`
   * / `noTrainers`, and this surface is create-only, so there is no edit path to keep open.
   */
  protected readonly nothingToPick = computed(
    () =>
      !this.loading() &&
      !this.loadFailed() &&
      (this.types().length === 0 || this.trainers().length === 0),
  );

  protected readonly canSubmit = computed(
    () => !!this.classTypeId() && !!this.instructorUserId() && !this.saving(),
  );

  ngOnInit(): void {
    // Not the constructor: a required signal input is not readable until the binding is set.
    this.durationMinutes.set(this.drawn().durationMinutes);
    void this.loadOptions();
  }

  private async loadOptions(): Promise<void> {
    try {
      // In parallel: neither depends on the other, and the overlay needs both before it can render.
      const [types, trainers] = await Promise.all([
        this.classTypes.getAll(),
        this.members.getTrainers(),
      ]);

      // getAll() is deliberately unfiltered — the types screen needs the retired ones — so the filter
      // is here, exactly as class-form does it. A retired type must not be newly attachable.
      this.types.set(types.filter((type) => type.isActive));
      this.trainers.set(trainers);
    } catch {
      this.loadFailed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  /** Copies the type's defaults, keeping the duration the gesture already expressed. */
  protected selectType(id: string): void {
    this.classTypeId.set(id);
    this.error.set(null);

    const type = this.types().find((candidate) => candidate.id === id);

    if (type) {
      this.capacity.set(type.defaultCapacity);
    }
  }

  protected async submit(): Promise<void> {
    if (!this.canSubmit()) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    try {
      await this.classes.create({
        classTypeId: this.classTypeId(),
        startsAt: this.drawn().startsAt.toISOString(),
        durationMinutes: this.durationMinutes(),
        instructorUserId: this.instructorUserId(),
        capacity: this.capacity(),
      });

      this.created.emit();
    } catch (failure) {
      const reason = ((failure as HttpErrorResponse)?.error as { reason?: string } | undefined)
        ?.reason;

      // The same words class-form uses for the same refusal — that is what classFailureMessage is for.
      this.error.set(classFailureMessage(reason));
    } finally {
      this.saving.set(false);
    }
  }

  protected close(): void {
    this.closed.emit();
  }
}
