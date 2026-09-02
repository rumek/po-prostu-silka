import { DatePipe, NgTemplateOutlet, isPlatformBrowser } from '@angular/common';
import {
  Component,
  LOCALE_ID,
  PLATFORM_ID,
  TemplateRef,
  computed,
  contentChild,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import {
  CalendarEvent,
  CalendarWeekViewComponent,
  DateAdapter,
  provideCalendar,
} from 'angular-calendar';
import { adapterFactory } from 'angular-calendar/date-adapters/date-fns';
import { addDays, startOfDay, startOfWeek } from 'date-fns';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { WEEK_VIEW_MEDIA_QUERY } from './calendar-breakpoint';

/** The window the calendar is currently showing, as UTC instants for the API. */
export interface CalendarRange {
  from: Date;
  to: Date;
}

/**
 * THE schedule calendar — one component, both screens (prd-v2 FR-017).
 *
 * <h2>Why this lives in `shared/`</h2>
 *
 * It is the first component in this app that belongs to neither `core/` (services, models and guards
 * — no components) nor a single feature. Putting it under `features/schedule/` would make the member
 * screen the owner of the admin panel's calendar, and the failure FR-017 names is precisely the two
 * drifting apart. So: a third category, with this component as its first inhabitant.
 *
 * <h2>What it owns and what it does not</h2>
 *
 * It owns navigation, the day/week choice, the mapping onto the library's events, and the empty
 * state. It owns NO data: it says which window it is showing via {@link rangeChange} and the parent
 * hands back the classes for it. That is what lets one component serve two endpoints with two
 * different policies.
 *
 * It also knows nothing about roles. Admin actions arrive as a projected template, so nothing
 * role-specific is compiled into the member's screen — the alternative, a `mode` input, is the
 * "widget with a dozen flags" the FR-017 shaping challenge called out by name.
 *
 * <h2>One renderer, two shapes</h2>
 *
 * Both the day and the week are `mwl-calendar-week-view` with `daysInWeek` set to 1 or 7. The library
 * ships a separate day-view component, but one renderer means one event template, one set of
 * styling overrides, and — from Phase 4 — one drag path. `viewDate` is bound to the range start
 * rather than to a free-floating "current date", so what the grid renders and what was fetched can
 * never disagree.
 *
 * <h2>Local clock in, UTC out</h2>
 *
 * Week boundaries are computed in the BROWSER's clock, like every other timestamp in this app. A
 * range computed in UTC would put Monday-00:00 an hour inside Sunday for half the year, and classes
 * at the edges of a week would silently land in the wrong one.
 */
@Component({
  imports: [CalendarWeekViewComponent, DatePipe, NgTemplateOutlet],
  // ON THE COMPONENT, NOT IN app.config.ts. Registering the adapter in the application providers
  // pulls angular-calendar into the INITIAL bundle - it did, and the budget caught it: +62 kB over
  // the 500 kB ceiling with the lazy chunks left nearly empty. Declared here, the library ships in
  // the chunk of whichever lazy route rendered it, and a screen that never shows a calendar never
  // downloads one.
  //
  // The date-fns adapter, not moment: moment is an optional peer this app does not install, and
  // date-fns v4 is a hard requirement of the library since 0.32.0. The adapter is stateless, so one
  // instance per calendar costs nothing.
  providers: [provideCalendar({ provide: DateAdapter, useFactory: adapterFactory })],
  selector: 'app-schedule-calendar',
  styleUrl: './schedule-calendar.scss',
  templateUrl: './schedule-calendar.html',
})
export class ScheduleCalendar {
  private readonly platformId = inject(PLATFORM_ID);

  /**
   * Taken from the app's LOCALE_ID rather than hardcoded 'pl'. app.config.ts sets that and registers
   * the matching CLDR data; hardcoding a locale here would mean this component demanding data nobody
   * promised to register - which is exactly what it did, and the specs caught it as NG0701.
   */
  protected readonly locale = inject(LOCALE_ID);

  readonly classes = input<ScheduledClass[]>([]);
  readonly loading = input(false);
  readonly loadFailed = input(false);

  /**
   * Suppresses the per-class action template (and, from Phase 4, the create gesture).
   *
   * Defaults to read-only: the member schedule is the surface that must never grow an action by
   * accident, so the admin panel is the one that has to opt in.
   */
  readonly readOnly = input(true);

  /** The visible window changed — fetch for it. Emitted on init too, which is the first load. */
  readonly rangeChange = output<CalendarRange>();

  /**
   * Per-class actions, projected by the screen that has any. Receives the `ScheduledClass` as
   * `$implicit`, so the caller gets the real row rather than the library's wrapper.
   */
  readonly classActions = contentChild<TemplateRef<{ $implicit: ScheduledClass }>>('classActions');

  /**
   * Which day the view starts on. Always a local midnight — the week view is bound to it directly,
   * so this is both "where we are" and "where the fetched window begins".
   */
  protected readonly anchor = signal(startOfDay(new Date()));

  /**
   * False until something measures the viewport.
   *
   * The initial value is what renders before the first `matchMedia` read AND what the specs see, since
   * jsdom provides no `matchMedia` at all. Day-first is the mobile-first answer: the narrow device,
   * which is also the slow one, gets the right view with no reflow.
   */
  protected readonly weekView = signal(false);

  protected readonly daysInWeek = computed(() => (this.weekView() ? 7 : 1));

  /** [from, to) in local time. The week starts Monday — Polish convention, and the club's. */
  protected readonly range = computed<CalendarRange>(() => {
    const from = this.weekView()
      ? startOfWeek(this.anchor(), { weekStartsOn: 1 })
      : startOfDay(this.anchor());

    return { from, to: addDays(from, this.daysInWeek()) };
  });

  protected readonly events = computed<CalendarEvent<ScheduledClass>[]>(() =>
    this.classes().map((row) => {
      const start = new Date(row.startsAt);

      return {
        id: row.id,
        start,
        // Derived, never stored — see the Class aggregate. The grid needs an end to size the block.
        end: new Date(start.getTime() + row.durationMinutes * 60_000),
        title: row.name,
        // The real row rides along so the projected action template and the tile get an object they
        // can read, rather than one reconstructed from the parts the library kept.
        meta: row,
      };
    }),
  );

  /**
   * "Nothing is scheduled here" — which is NOT the same statement as "still loading" or "it failed",
   * and must not look like either (prd-v2 §Success Criteria, the empty-schedule guardrail).
   */
  protected readonly isEmpty = computed(
    () => !this.loading() && !this.loadFailed() && this.classes().length === 0,
  );

  /** What the `<input type="date">` shows: the anchor as a local `YYYY-MM-DD`. */
  protected readonly anchorInputValue = computed(() => {
    const day = this.anchor();
    const pad = (value: number) => String(value).padStart(2, '0');

    // Local getters deliberately — the UTC ones would show the wrong day either side of midnight.
    return `${day.getFullYear()}-${pad(day.getMonth() + 1)}-${pad(day.getDate())}`;
  });

  constructor() {
    // The ONLY place the viewport is measured. Guarded even though nothing renders on a server
    // today: main.server.ts and app.routes.server.ts are present and one angular.json key away from
    // being live, and an unguarded matchMedia would turn that switch-on into a crash three files
    // from the change that caused it.
    // `typeof` rather than a bare platform check: being in a browser platform does NOT guarantee the
    // API. jsdom, which the specs run in, is a browser platform with no matchMedia at all - so the
    // day-first default this component documents has to actually survive its absence, not just be
    // claimed.
    if (isPlatformBrowser(this.platformId) && typeof window.matchMedia === 'function') {
      const query = window.matchMedia(WEEK_VIEW_MEDIA_QUERY);

      this.weekView.set(query.matches);
      query.addEventListener('change', (event) => this.weekView.set(event.matches));
    }

    // Emits on creation too — that first emission is what triggers the initial load, so the parent
    // needs no ngOnInit of its own.
    effect(() => this.rangeChange.emit(this.range()));
  }

  protected stepDays(days: number): void {
    this.anchor.update((current) => addDays(current, days));
  }

  /** One week in either direction, whichever view is showing. */
  protected stepWeeks(weeks: number): void {
    this.stepDays(weeks * 7);
  }

  protected goToToday(): void {
    this.anchor.set(startOfDay(new Date()));
  }

  protected jumpTo(value: string): void {
    if (!value) {
      return;
    }

    // Split rather than `new Date(value)`: a bare `YYYY-MM-DD` is parsed as UTC midnight, which lands
    // on the previous day for anyone behind UTC. The parts are a local date by construction.
    const [year, month, day] = value.split('-').map(Number);

    this.anchor.set(new Date(year, month - 1, day));
  }
}
