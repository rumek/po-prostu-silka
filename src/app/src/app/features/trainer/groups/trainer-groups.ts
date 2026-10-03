import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { classifyFailure } from '../../../core/http/failure';
import { transportMessage } from '../../../core/http/transport-messages';
import { TrainerGroup } from '../../../core/scheduling/roster.models';
import { RosterService } from '../../../core/scheduling/roster.service';
import { Empty } from '../../../shared/forms/empty/empty';
import { createLoadFence } from '../../../shared/forms/load-fence';
import { Loading } from '../../../shared/forms/loading/loading';
import { Icon } from '../../../shared/icons/icon';
import { List } from '../../../shared/list/list';
import { Row } from '../../../shared/list/row';

/**
 * The trainer's "Grupy" (S-37): the groups whose upcoming classes they instruct, each leading to its
 * roster. In Więcej, because the trainer's bottom bar is full since S-36.
 *
 * The list is exactly the set the API lets the trainer manage — both read the same rule ("instructs an
 * upcoming class of the group") — so a row never leads to a 403.
 */
@Component({
  imports: [DatePipe, Empty, Icon, List, Loading, Row, RouterLink],
  selector: 'app-trainer-groups',
  styleUrl: './trainer-groups.scss',
  templateUrl: './trainer-groups.html',
})
export class TrainerGroups implements OnInit {
  private readonly rosters = inject(RosterService);

  protected readonly rows = signal<TrainerGroup[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadMessage = signal<string | null>(null);
  private readonly fence = createLoadFence();

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();
    this.loading.set(true);
    this.loadMessage.set(null);

    try {
      const rows = await this.rosters.trainerGroups();
      if (this.fence.isCurrent(generation)) {
        this.rows.set(rows);
      }
    } catch (failure) {
      if (this.fence.isCurrent(generation)) {
        this.loadMessage.set(
          transportMessage(classifyFailure(failure)) ?? 'Nie udało się wczytać grup.',
        );
      }
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.loading.set(false);
      }
    }
  }
}
