import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TrainingPlanService } from '../../../core/training/training-plan.service';
import { TrainingPlanSummary } from '../../../core/training/training-plan.models';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { createLoadFence } from '../../../shared/forms/load-fence';
import { Loading } from '../../../shared/forms/loading/loading';
import { Empty } from '../../../shared/forms/empty/empty';
import { List } from '../../../shared/list/list';
import { Row } from '../../../shared/list/row';

/**
 * Every active training plan in the club (prd.md FR-015, FR-016).
 *
 * ONE ROW PER MEMBER, always — the API lists only active plans, and a member holds at most one.
 * There is no "archived plans" view: superseded plans are kept so the member's history is not
 * rewritten, not so anyone browses them, and adding a screen for them would invite editing one.
 *
 * The search is a pure client filter over rows already held, matching exercises.ts — the API returns
 * everything, and a club's worth of plans is a list a browser filters instantly.
 */
@Component({
  imports: [Row, List, Empty, Loading, DatePipe, RouterLink],
  selector: 'app-plans',
  styleUrl: './plans.scss',
  templateUrl: './plans.html',
})
export class Plans implements OnInit {
  private readonly plans = inject(TrainingPlanService);

  /** Everything the API returned, unfiltered. `visible` is what the template renders. */
  protected readonly rows = signal<TrainingPlanSummary[]>([]);

  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);

  /**
   * WHY the load failed, when the transport can say (S-19).
   *
   * The screen state itself is unchanged — this is still outlet 4, and the sentence in the template
   * still names what could not be fetched. What is new is the second half: a dead network now says
   * so, where before a killed API read exactly like a server that answered and refused.
   */
  protected readonly loadMessage = signal<string | null>(null);

  protected readonly search = signal('');

  /** Matches on member name OR plan name: a trainer looks for a person, sometimes for a programme. */
  protected readonly visible = computed(() => {
    const term = this.search().trim().toLocaleLowerCase('pl');
    if (term.length === 0) {
      return this.rows();
    }

    return this.rows().filter(
      (row) =>
        row.memberDisplayName.toLocaleLowerCase('pl').includes(term) ||
        row.name.toLocaleLowerCase('pl').includes(term),
    );
  });

  /** True when plans exist but the search hides every one — a different message from "none yet". */
  protected readonly hiddenByFilter = computed(
    () => this.rows().length > 0 && this.visible().length === 0,
  );

  /** See members.ts — nothing cancels an in-flight request, so the last RESPONSE would otherwise win. */
  private readonly fence = createLoadFence();

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();

    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const rows = await this.plans.getAll();
      if (!this.fence.isCurrent(generation)) {
        return;
      }
      this.rows.set(rows);
    } catch (failure) {
      if (!this.fence.isCurrent(generation)) {
        return;
      }
      this.loadMessage.set(transportMessage(classifyFailure(failure)));
      this.loadFailed.set(true);
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.loading.set(false);
      }
    }
  }

  protected onSearch(value: string): void {
    this.search.set(value);
  }

  /**
   * Polish plurals, which have three forms and not two: 1 "ćwiczenie", 2-4 "ćwiczenia", otherwise
   * "ćwiczeń" - and the teens (12, 13, 14) take the last form despite ending in 2-4.
   *
   * Written out rather than pulled from Angular's i18n plural machinery, which D9 rules out along
   * with the rest of the translation stack. One noun in one screen does not justify it.
   */
  protected exerciseNoun(count: number): string {
    if (count === 1) {
      return 'ćwiczenie';
    }

    const lastTwo = count % 100;
    const last = count % 10;

    if (last >= 2 && last <= 4 && !(lastTwo >= 12 && lastTwo <= 14)) {
      return 'ćwiczenia';
    }

    return 'ćwiczeń';
  }
}
