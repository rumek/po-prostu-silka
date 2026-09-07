import { DatePipe } from '@angular/common';
import { Component, input } from '@angular/core';

/**
 * A training plan's identity: its name, who assigned it, and when.
 *
 * THE HEADING LEVEL IS AN INPUT, and that is the only reason this needed thought. On `/my-plan` the
 * plan name IS the page heading, so it is an `h1`. On the dashboard the same block is one card among
 * several under the dashboard's own `h1`, where a second `h1` would flatten the document outline for
 * anyone navigating by heading. Hard-coding either level breaks the other caller.
 *
 * Only the two levels actually needed are offered. A free `number` input would invite `h4` on a page
 * with no `h3`, which is the outline bug this input exists to prevent.
 */
@Component({
  imports: [DatePipe],
  selector: 'app-plan-summary',
  styleUrl: './plan-summary.scss',
  templateUrl: './plan-summary.html',
})
export class PlanSummary {
  readonly name = input.required<string>();

  /** Display name of the trainer or admin who assigned it (FR-016). */
  readonly assignedByDisplayName = input.required<string>();

  /** ISO-8601 UTC, as the API returns it. */
  readonly createdAt = input.required<string>();

  /** `h1` on the screen the plan owns, `h2` in a dashboard card. Defaults to the page-heading case. */
  readonly headingLevel = input<'h1' | 'h2'>('h1');
}
