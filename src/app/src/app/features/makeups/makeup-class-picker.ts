import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { classifyFailure } from '../../core/http/failure';
import { transportMessage } from '../../core/http/transport-messages';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { makeupFailureMessage } from '../../core/scheduling/makeup-failure';
import { MakeupItem } from '../../core/scheduling/makeup.models';
import { MakeupService } from '../../core/scheduling/makeup.service';
import { Empty } from '../../shared/forms/empty/empty';
import { Loading } from '../../shared/forms/loading/loading';
import { createLoadFence } from '../../shared/forms/load-fence';
import { useOverlayFocus } from '../../shared/forms/overlay-focus';
import { Icon } from '../../shared/icons/icon';
import { List } from '../../shared/list/list';
import { Row } from '../../shared/list/row';
import { OverlayHead } from '../../shared/overlay/overlay-head';
import { clubDay, instantWhen } from './makeup-format';

/**
 * Chooses the class a makeup goes into (S-36).
 *
 * <h2>Only what the booking would accept</h2>
 *
 * The list comes from the server already narrowed — upcoming, by the deadline, with a free spot,
 * without the member, on a day one of their karnets covers, from ANY trainer — so a refusal here is
 * a lost race rather than a normal outcome. It still happens, and stays in the overlay as a banner
 * (S-19 outlet 2: the submission as a whole failed), with the list reloaded under it.
 *
 * <h2>One tap per class, no confirm foot</h2>
 *
 * Each row books with its own button: the overlay holds a choice of classes, not a form, so a separate
 * select-then-confirm step would only add a tap. With nothing to confirm, the overlay has no
 * `.overlay-actions` (AGENTS.md "Every overlay has one shape").
 */
@Component({
  host: { '(document:keydown.escape)': 'close()' },
  imports: [OverlayHead, List, Row, Empty, Loading, Icon],
  selector: 'app-makeup-class-picker',
  styleUrl: './makeup-class-picker.scss',
  templateUrl: './makeup-class-picker.html',
})
export class MakeupClassPicker implements OnInit {
  private readonly focus = useOverlayFocus(() => this.close());
  private readonly makeups = inject(MakeupService);

  readonly item = input.required<MakeupItem>();

  /** The makeup was booked; carries the item as the server now reports it. */
  readonly booked = output<MakeupItem>();
  readonly closed = output<void>();

  protected readonly classes = signal<ScheduledClass[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly loadMessage = signal<string | null>(null);

  /** The class being booked, so only its button says so and the rest wait. */
  protected readonly booking = signal<string | null>(null);
  protected readonly failure = signal<string | null>(null);

  private readonly fence = createLoadFence();

  protected readonly clubDay = clubDay;
  protected readonly when = instantWhen;

  ngOnInit(): void {
    void this.load();
  }

  protected async load(): Promise<void> {
    const generation = this.fence.begin();
    this.loading.set(true);
    this.loadFailed.set(false);

    try {
      const classes = await this.makeups.classes(this.item().absenceBookingId);
      if (this.fence.isCurrent(generation)) {
        this.classes.set(classes);
      }
    } catch (error) {
      if (!this.fence.isCurrent(generation)) {
        return;
      }

      // Outlet 4: nothing to choose from. A named refusal (the item stopped being open) takes the
      // makeup table's words; anything else keeps the overlay's own sentence and its retry.
      const info = classifyFailure(error);
      this.loadMessage.set(
        transportMessage(info) ??
          (info.kind === 'business' ? makeupFailureMessage(info.reason) : null),
      );
      this.loadFailed.set(true);
    } finally {
      if (this.fence.isCurrent(generation)) {
        this.loading.set(false);
      }
    }
  }

  protected async book(target: ScheduledClass): Promise<void> {
    if (this.booking()) {
      return;
    }

    this.booking.set(target.id);
    this.failure.set(null);

    try {
      this.booked.emit(await this.makeups.book(this.item().absenceBookingId, target.id));
    } catch (error) {
      const info = classifyFailure(error);
      this.failure.set(transportMessage(info) ?? makeupFailureMessage(info.reason));
      void this.load();
    } finally {
      this.booking.set(null);
    }
  }

  protected close(): void {
    this.closed.emit();
  }
}
