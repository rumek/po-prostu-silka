import { Component, computed, inject, input } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { ExerciseSummary } from '../../core/training/exercise.models';
import { embedUrl, isVideoId } from '../../core/training/youtube';
import { Icon, IconName } from '../icons/icon';

/** One of the exercise's one-line facts, as the strip under the video draws it. */
interface Fact {
  readonly icon: IconName;
  readonly label: string;
  readonly value: string;
}

/** One of the prose instructions, in the order a member reads them before a set. */
interface Guide {
  readonly heading: string;
  readonly text: string;
}

/**
 * An exercise's body, below its heading: the video, the facts and the instructions. The admin's
 * detail screen and the member's plan-exercise screen both render it, so the two cannot drift — they
 * were byte-identical twin templates until the redesign put them in the plan card's shape.
 *
 * <p>The HEADER stays the caller's: the admin gets an edit button, the member a way back to the plan.
 * So does the EMPTY STATE, projected: a bare exercise tells the admin how to fix it and tells the
 * member who to ask, and those are different sentences. It shows only when there is nothing else.</p>
 *
 * <p>The drawing is the plan card's (features/my-plan): white cards, the parameters' strip of
 * icon-over-label-over-value columns for the facts, and the class card's capitals line for each
 * instruction's heading.</p>
 */
@Component({
  imports: [Icon],
  selector: 'app-exercise-view',
  styleUrl: './exercise-view.scss',
  templateUrl: './exercise-view.html',
})
export class ExerciseView {
  private readonly sanitizer = inject(DomSanitizer);

  readonly exercise = input.required<ExerciseSummary>();

  /**
   * No video renders nothing at all — not an empty frame, which would read as a failure.
   *
   * THIS IS THE ONLY bypassSecurityTrust* CALL IN THE APP, and the isVideoId guard is why it is safe:
   * the id is re-checked against the same 11-character pattern the server enforces immediately
   * before it is trusted. Computed rather than a template getter on purpose — a getter would re-trust
   * the value on every change detection cycle.
   */
  protected readonly playerUrl = computed<SafeResourceUrl | null>(() => {
    const videoId = this.exercise().videoId;

    return isVideoId(videoId)
      ? this.sanitizer.bypassSecurityTrustResourceUrl(embedUrl(videoId))
      : null;
  });

  protected readonly facts = computed<Fact[]>(() => {
    const row = this.exercise();
    const all: (Fact | null)[] = [
      row.muscleGroup ? { icon: 'target', label: 'Grupa mięśniowa', value: row.muscleGroup } : null,
      row.difficulty ? { icon: 'level', label: 'Trudność', value: row.difficulty } : null,
      row.equipment ? { icon: 'kettlebell', label: 'Sprzęt', value: row.equipment } : null,
    ];

    return all.filter((fact): fact is Fact => fact !== null);
  });

  protected readonly guide = computed<Guide[]>(() => {
    const row = this.exercise();
    const all: [string, string | null][] = [
      ['Opis', row.description],
      ['Przygotowanie', row.preparation],
      ['Pozycja startowa', row.startingPosition],
      ['Wykonanie', row.execution],
    ];

    return all
      .filter((entry): entry is [string, string] => !!entry[1])
      .map(([heading, text]) => ({ heading, text }));
  });

  /** Saying "nothing here" beats saying nothing: a bare entry looks identical to a broken screen. */
  protected readonly bare = computed(
    () => !this.playerUrl() && this.facts().length === 0 && this.guide().length === 0,
  );
}
