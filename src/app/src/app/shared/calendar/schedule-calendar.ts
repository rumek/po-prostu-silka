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
  CalendarDateFormatter,
  CalendarEvent,
  CalendarEventTimesChangedEvent,
  CalendarWeekViewComponent,
  DateAdapter,
  provideCalendar,
} from 'angular-calendar';
import { adapterFactory } from 'angular-calendar/date-adapters/date-fns';
import { addDays, addMinutes, isSameDay, setHours, startOfDay, startOfWeek } from 'date-fns';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { WEEK_VIEW_MEDIA_QUERY } from './calendar-breakpoint';
import { PolishCalendarDateFormatter } from './polish-date-formatter';

/** A time range drawn on the grid, ready for the overlay that will turn it into a class. */
export interface DrawnRange {
  startsAt: Date;
  durationMinutes: number;
}

/**
 * An existing class moved or resized on the grid (prd-v2 FR-019).
 *
 * Carries the class it happened to, so the screen can send back the fields the gesture cannot touch —
 * type, trainer, capacity — unchanged. The two it CAN touch arrive here already snapped to the grid.
 */
export interface RescheduledClass {
  class: ScheduledClass;
  startsAt: Date;
  durationMinutes: number;
}

/**
 * How long one row of the grid is. Two segments per hour is the library's default and this binds it
 * explicitly, because the drag gesture snaps to it: the drawn duration is always a whole number of
 * segments, so a stray click cannot produce a zero-minute class.
 */
const SEGMENT_MINUTES = 30;

/**
 * The hours the grid renders: 06:00 up to 24:00.
 *
 * A full 24-hour grid spent a third of its height on hours the club is shut, which on a phone is the
 * difference between seeing the evening classes and scrolling for them. `DAY_END_HOUR` is the last
 * hour DRAWN, not an exclusive bound — 23 renders the 23:00–24:00 row and stops there.
 *
 * Nothing outside this window is reachable, so a class scheduled at 05:00 would be invisible here.
 * That is a deliberate bet on the club's opening hours; widen these two constants if it stops
 * holding.
 */
const DAY_START_HOUR = 6;
const DAY_END_HOUR = 23;

/**
 * The day strip's labels, Monday first.
 *
 * Written out rather than formatted from the locale on purpose. CLDR's Polish abbreviations are
 * "pon.", "wt.", "śr." — four characters with a full stop, which do not fit seven buttons plus two
 * arrows across a phone, and the narrow forms collapse Monday and Wednesday onto the same letter as
 * Tuesday and Saturday respectively. These are the two-letter forms Polish actually uses on a
 * calendar. The library's own rendering still goes through LOCALE_ID; this is chrome, not data.
 */
const WEEKDAY_LABELS = ['Pn', 'Wt', 'Śr', 'Cz', 'Pt', 'So', 'Nd'];

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
  providers: [
    provideCalendar(
      { provide: DateAdapter, useFactory: adapterFactory },
      // The library's own formatter writes American time and date patterns whatever the locale — see
      // PolishCalendarDateFormatter.
      { dateFormatter: { provide: CalendarDateFormatter, useClass: PolishCalendarDateFormatter } },
    ),
  ],
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
   * The admin drew a time range on empty grid (prd-v2 FR-019). Carries only what the GESTURE can
   * know — when and how long. The class type and the trainer are the screen's problem, because a
   * pointer cannot express them.
   *
   * Never emitted while {@link readOnly}, which is what keeps the member schedule and past weeks
   * inert without either of them knowing this output exists.
   */
  readonly rangeDrawn = output<DrawnRange>();

  /**
   * An existing class was dragged to a new time or resized (prd-v2 FR-019).
   *
   * Like {@link rangeDrawn}, this reports a GESTURE and writes nothing: the screen owns the class
   * list and the API call. Until the screen updates that list the library puts the block back where
   * it was, which is what makes a refused move visibly a refused move.
   *
   * Gated on {@link readOnly} through the events themselves — a read-only calendar marks nothing
   * draggable or resizable, so there is no gesture to emit.
   */
  readonly classRescheduled = output<RescheduledClass>();

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

  /**
   * The seven days of the week the anchor falls in — the day view's navigation.
   *
   * A day at a time is the right amount of schedule on a phone, but it is a poor way to MOVE: getting
   * to Friday meant four presses of an arrow, or the date picker. The strip makes the week the unit
   * you navigate and the day the unit you read.
   *
   * Only rendered in the day view. Above the breakpoint the week is already on screen, and a strip
   * pointing at the column beside it would be navigation to where you already are.
   */
  protected readonly weekDays = computed(() => {
    const start = startOfWeek(this.anchor(), { weekStartsOn: 1 });
    const selected = this.anchor().getTime();
    const today = startOfDay(new Date()).getTime();

    return WEEKDAY_LABELS.map((label, index) => {
      const date = addDays(start, index);

      return {
        date,
        label,
        isSelected: date.getTime() === selected,
        isToday: date.getTime() === today,
      };
    });
  });

  protected readonly events = computed<CalendarEvent<ScheduledClass>[]>(() =>
    this.classes().map((row) => {
      const start = new Date(row.startsAt);
      // History is not rearrangeable — the same rule the create gesture applies to the segment it is
      // pressed on, applied to the class itself. A class already under way is included: moving it now
      // would move something members are standing in.
      const editable = !this.readOnly() && start.getTime() > Date.now();

      return {
        id: row.id,
        start,
        // Derived, never stored — see the Class aggregate. The grid needs an end to size the block.
        end: new Date(start.getTime() + row.durationMinutes * 60_000),
        title: row.name,
        // Both edges resize, so the admin can pull the start earlier or the end later — the two ways
        // of saying "this class is longer". Off entirely on a read-only calendar, which is what keeps
        // the member's schedule and past weeks inert.
        draggable: editable,
        resizable: { beforeStart: editable, afterEnd: editable },
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

  /** Ends of the segment range being drawn, or null when no gesture is in flight. */
  private readonly drawFrom = signal<Date | null>(null);
  private readonly drawTo = signal<Date | null>(null);

  /** Set when a press lands in the past; cleared by the next legal gesture or by the close button. */
  protected readonly pastRefusal = signal(false);

  protected readonly segmentMinutes = SEGMENT_MINUTES;
  protected readonly dayStartHour = DAY_START_HOUR;
  protected readonly dayEndHour = DAY_END_HOUR;

  /**
   * The range currently under the pointer, as the class it would become.
   *
   * The same shape {@link rangeDrawn} emits, and computed once: the preview the admin sees and the
   * value the overlay is prefilled with are then the same number by construction, not two
   * calculations that have to agree.
   */
  protected readonly draft = computed<DrawnRange | null>(() => {
    const from = this.drawFrom();
    const to = this.drawTo();

    if (!from || !to) {
      return null;
    }

    const startsAt = new Date(Math.min(from.getTime(), to.getTime()));
    const lastSegment = Math.max(from.getTime(), to.getTime());

    // The drawn range covers the last segment too, so a single click is one segment rather than zero
    // minutes — the class the API would refuse as invalid_duration.
    return {
      startsAt,
      durationMinutes: (lastSegment - startsAt.getTime()) / 60_000 + SEGMENT_MINUTES,
    };
  });

  /** Where the preview block ends. Only for the label — the emitted range carries a duration. */
  protected readonly draftEndsAt = computed(() => {
    const draft = this.draft();

    return draft && new Date(draft.startsAt.getTime() + draft.durationMinutes * 60_000);
  });

  /** How many segments tall the preview block is, so it can be sized off `segmentHeight`. */
  protected readonly draftSegments = computed(() => {
    const draft = this.draft();

    return draft ? draft.durationMinutes / SEGMENT_MINUTES : 0;
  });

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

  /**
   * Whether this segment is where the preview block starts.
   *
   * The preview is ONE element anchored on the first segment and sized across the rest, not a class
   * per highlighted segment: a run of highlighted segments carries the grid's own hour lines through
   * the middle of it, which reads as several half-hour blocks rather than as the one class it is
   * about to become.
   */
  protected isDraftStart(date: Date): boolean {
    return this.draft()?.startsAt.getTime() === date.getTime();
  }

  /** Already been and gone — a class cannot start here (prd-v2 FR-019, and the API's `starts_in_past`). */
  protected isPastSegment(date: Date): boolean {
    return date.getTime() < Date.now();
  }

  protected dismissRefusal(): void {
    this.pastRefusal.set(false);
  }

  /**
   * Whether a move or resize keeps the whole class inside the rendered grid.
   *
   * Bound to the library's `validateEventTimesChanged`, which is consulted WHILE the pointer moves:
   * refusing here means the block never travels past the edge in the first place, rather than
   * springing back from somewhere it should not have been able to reach. Without it a class could be
   * dragged above 06:00 or resized past midnight and simply disappear off the grid — still there, at
   * a time nothing in this view can show.
   *
   * An arrow property, not a method: the library stores the reference and calls it unbound.
   */
  protected readonly fitsInGrid = (change: CalendarEventTimesChangedEvent): boolean => {
    const endsAt = change.newEnd;

    if (!endsAt) {
      return false;
    }

    // Derived from the day the class would LAND on, so a drag across midnight is measured against the
    // right day's opening and closing.
    const day = startOfDay(change.newStart);
    const opens = setHours(day, DAY_START_HOUR);
    // DAY_END_HOUR is the last hour drawn, so the grid closes an hour after it starts. Built by
    // adding minutes rather than by writing hour 24, which no clock has.
    const closes = addMinutes(setHours(day, DAY_END_HOUR), 60);

    return change.newStart >= opens && endsAt <= closes;
  };

  /**
   * A class was dropped somewhere new, or one of its edges was pulled.
   *
   * Both gestures arrive here — the library distinguishes them with `type`, and this does not need
   * to: a move and a resize both come down to a new start and a new duration, which is exactly what
   * the update endpoint takes. Snapping to the half-hour grid has already happened.
   *
   * The past is refused the same way a press on a past segment is, and for the same reason: the API
   * answers `starts_in_past`, and hearing that after the block has visibly moved is worse than not
   * being able to put it there. Emitting nothing leaves the class list untouched, and the library
   * then puts the block back where it was.
   */
  protected applyTimesChanged(change: CalendarEventTimesChangedEvent<ScheduledClass>): void {
    const row = change.event.meta;
    const startsAt = change.newStart;
    const endsAt = change.newEnd;

    if (!row || !endsAt) {
      return;
    }

    if (startsAt.getTime() < Date.now()) {
      this.pastRefusal.set(true);
      return;
    }

    // Checked again on the drop, not only during the drag: `fitsInGrid` governs where the pointer may
    // take the block, and this governs what is allowed to be written. A gesture that arrives here out
    // of bounds is dropped silently — the pointer was already prevented from going there, so there is
    // nothing to explain.
    if (!this.fitsInGrid(change)) {
      return;
    }

    const durationMinutes = (endsAt.getTime() - startsAt.getTime()) / 60_000;

    // A resize that collapsed the block to nothing is not a class the API would accept
    // (`invalid_duration`); dropping it here means the block simply springs back.
    if (durationMinutes < SEGMENT_MINUTES) {
      return;
    }

    this.pastRefusal.set(false);
    this.classRescheduled.emit({ class: row, startsAt, durationMinutes });
  }

  /**
   * Begins a drag-to-create gesture on an empty segment.
   *
   * Events are painted ABOVE the segments, so a press that lands on an existing class never reaches
   * here — the "don't start on an existing class" rule needs no code of its own.
   *
   * The move and release listeners go on the document rather than the segment: a drag that leaves the
   * grid, or releases outside the window, still has to end cleanly rather than leave the calendar
   * stuck mid-gesture.
   */
  protected startDraw(date: Date, event: MouseEvent): void {
    // Left button only; a right-click is a context menu, not a gesture.
    if (this.readOnly() || event.button !== 0) {
      return;
    }

    event.preventDefault();

    // Refused ON THE GESTURE, before any form exists to fill in: nothing is drawn, {@link rangeDrawn}
    // never fires, and no overlay opens. The API's `starts_in_past` stays as the backstop for time
    // passing while an overlay is already open — it is just no longer the first the admin hears of it.
    if (this.isPastSegment(date)) {
      this.pastRefusal.set(true);
      return;
    }

    this.pastRefusal.set(false);
    this.drawFrom.set(date);
    this.drawTo.set(date);

    const move = (moved: MouseEvent) => {
      const over = this.segmentAt(moved);

      // Same day only. A drag that wanders into the next column would otherwise produce a range
      // spanning the nights in between — a preview taller than the grid, and a class of many hours
      // nobody meant to ask for.
      if (over && isSameDay(over, date)) {
        this.drawTo.set(over);
      }
    };

    const release = () => {
      document.removeEventListener('mousemove', move);
      document.removeEventListener('mouseup', release);
      this.finishDraw();
    };

    document.addEventListener('mousemove', move);
    document.addEventListener('mouseup', release);
  }

  /** The segment under the pointer, read off the DOM rather than computed from pixel offsets. */
  private segmentAt(event: MouseEvent): Date | null {
    const element = document
      .elementFromPoint(event.clientX, event.clientY)
      ?.closest('[data-segment]');

    const iso = element?.getAttribute('data-segment');

    return iso ? new Date(iso) : null;
  }

  private finishDraw(): void {
    const draft = this.draft();

    this.drawFrom.set(null);
    this.drawTo.set(null);

    if (draft) {
      this.rangeDrawn.emit(draft);
    }
  }

  protected stepDays(days: number): void {
    this.anchor.update((current) => addDays(current, days));
  }

  /** One week in either direction, whichever view is showing. */
  protected stepWeeks(weeks: number): void {
    this.stepDays(weeks * 7);
  }

  /** Jump straight to a day in the strip's week. */
  protected goToDay(date: Date): void {
    this.anchor.set(startOfDay(date));
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
