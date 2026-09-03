import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { addDays } from 'date-fns';
import { CalendarEventTimesChangedEventType, CalendarWeekViewComponent } from 'angular-calendar';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { CalendarRange, DrawnRange, RescheduledClass, ScheduleCalendar } from './schedule-calendar';

/** Builds a class starting at a LOCAL wall-clock time, expressed as the UTC instant the API sends. */
function at(local: string, over: Partial<ScheduledClass> = {}): ScheduledClass {
  return {
    id: over.id ?? local,
    classTypeId: over.classTypeId ?? 't1',
    name: over.name ?? 'Joga',
    description: over.description ?? null,
    startsAt: new Date(local).toISOString(),
    durationMinutes: over.durationMinutes ?? 60,
    instructorUserId: over.instructorUserId ?? 'u1',
    instructor: over.instructor ?? 'Ola',
    capacity: over.capacity ?? 20,
    freeSpots: over.freeSpots ?? 20,
    status: 'Scheduled',
  };
}

/**
 * Stubs `matchMedia`, which jsdom does not implement. That absence is not incidental to this suite —
 * it is exactly the environment the component's day-first default exists for, so the "no matchMedia"
 * case below deliberately leaves it unstubbed.
 */
function stubMatchMedia(matches: boolean): (matches: boolean) => void {
  let listener: ((event: MediaQueryListEvent) => void) | null = null;

  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: () => ({
      matches,
      addEventListener: (_: string, handler: (event: MediaQueryListEvent) => void) => {
        listener = handler;
      },
      removeEventListener: () => {
        listener = null;
      },
    }),
  });

  return (next: boolean) => listener?.({ matches: next } as MediaQueryListEvent);
}

/** Hosts the calendar the way a screen does, so content projection is exercised rather than bypassed. */
@Component({
  imports: [ScheduleCalendar],
  template: `
    <app-schedule-calendar
      [classes]="classes()"
      [loading]="loading()"
      [loadFailed]="loadFailed()"
      [readOnly]="readOnly()"
      (rangeChange)="ranges.push($event)"
      (rangeDrawn)="drawn.push($event)"
      (classRescheduled)="rescheduled.push($event)"
    >
      <button calendarHeaderActions type="button" class="host-header-action">Dodaj</button>

      <ng-template #classActions let-row>
        <button type="button" class="host-row-action">Edytuj {{ row.name }}</button>
      </ng-template>
    </app-schedule-calendar>
  `,
})
class Host {
  readonly classes = signal<ScheduledClass[]>([]);
  readonly loading = signal(false);
  readonly loadFailed = signal(false);
  readonly readOnly = signal(true);
  readonly ranges: CalendarRange[] = [];
  readonly drawn: DrawnRange[] = [];
  readonly rescheduled: RescheduledClass[] = [];
}

describe('ScheduleCalendar', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;
  let restoreMatchMedia: PropertyDescriptor | undefined;

  beforeEach(() => {
    restoreMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');
  });

  afterEach(() => {
    if (restoreMatchMedia) {
      Object.defineProperty(window, 'matchMedia', restoreMatchMedia);
    } else {
      delete (window as unknown as Record<string, unknown>)['matchMedia'];
    }
  });

  function create(): void {
    TestBed.configureTestingModule({ imports: [Host] });
    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function click(selector: string): void {
    element().querySelector<HTMLButtonElement>(selector)!.click();
    fixture.detectChanges();
  }

  function lastRange(): CalendarRange {
    return host.ranges[host.ranges.length - 1];
  }

  /** Moves the view to a given local day. The day view has no day arrows — the strip is its navigation. */
  function goTo(date: Date): void {
    const pad = (value: number) => String(value).padStart(2, '0');
    const input = element().querySelector<HTMLInputElement>('.calendar-jump input')!;

    input.value = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  function daysFromNow(days: number): Date {
    const now = new Date();

    return new Date(now.getFullYear(), now.getMonth(), now.getDate() + days);
  }

  /**
   * The seven weekday buttons, without the arrows either side of them.
   *
   * They are deliberately the same control — same class, same size — so the position in the strip is
   * what tells them apart, exactly as it does for the reader.
   */
  function stripDays(): HTMLButtonElement[] {
    return [
      ...element().querySelectorAll<HTMLButtonElement>('app-calendar-week-strip .strip-button'),
    ].slice(1, -1);
  }

  // --- the view mode --------------------------------------------------------

  it('renders a single day when nothing can measure the viewport', () => {
    // jsdom has no matchMedia. Same shape as the very first paint in a browser, before any read.
    create();

    const range = lastRange();

    expect(range.to.getTime() - range.from.getTime()).toBe(24 * 60 * 60 * 1000);
  });

  it('renders a single day below the breakpoint and the whole week above it', () => {
    const emit = stubMatchMedia(true);
    create();

    let range = lastRange();
    expect(range.to.getTime() - range.from.getTime()).toBe(7 * 24 * 60 * 60 * 1000);

    // A week always starts on Monday — Polish convention.
    expect(range.from.getDay()).toBe(1);

    emit(false);
    fixture.detectChanges();

    range = lastRange();
    expect(range.to.getTime() - range.from.getTime()).toBe(24 * 60 * 60 * 1000);
  });

  // --- the range ------------------------------------------------------------

  it('emits a range on creation, so the parent needs no separate first fetch', () => {
    create();

    expect(host.ranges.length).toBe(1);
  });

  it('starts the range at local midnight, not at a UTC boundary', () => {
    create();

    const { from } = lastRange();

    expect(from.getHours()).toBe(0);
    expect(from.getMinutes()).toBe(0);
    expect(from.getSeconds()).toBe(0);
  });

  it('moves by a week in both directions', () => {
    create();

    const start = lastRange().from.getTime();
    const week = 7 * 24 * 60 * 60 * 1000;

    click('[aria-label="Następny tydzień"]');
    expect(lastRange().from.getTime()).toBe(start + week);

    click('[aria-label="Poprzedni tydzień"]');
    expect(lastRange().from.getTime()).toBe(start);
  });

  it('jumps to a typed date as a LOCAL day', () => {
    create();

    const input = element().querySelector<HTMLInputElement>('.calendar-jump input')!;
    input.value = '2026-03-04';
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const { from } = lastRange();

    // A bare YYYY-MM-DD parsed as UTC would land on the 3rd for anyone behind UTC.
    expect(from.getFullYear()).toBe(2026);
    expect(from.getMonth()).toBe(2);
    expect(from.getDate()).toBe(4);
  });

  it('gives each view exactly one navigation', () => {
    const emit = stubMatchMedia(true);
    create();

    // The week view keeps the toolbar: arrows and "Dziś", with no strip.
    expect(element().querySelector('app-calendar-week-strip')).toBeNull();
    expect(element().querySelector('.calendar-today')).not.toBeNull();

    emit(false);
    fixture.detectChanges();

    // The day view is the strip and nothing else. Day arrows and "Dziś" are gone: two navigations for
    // one view is how a toolbar stops being read at all.
    expect(element().querySelector('app-calendar-week-strip')).not.toBeNull();
    expect(element().querySelector('.calendar-nav')).toBeNull();
    expect(element().querySelector('.calendar-today')).toBeNull();
    expect(element().querySelectorAll('[aria-label="Następny tydzień"]').length).toBe(1);
  });

  // --- the week that is not 168 hours long ----------------------------------

  /*
   * 2026-10-25 is the Sunday Poland puts its clocks back, so that day is 25 hours long and the week
   * containing it is 169. These two cases are timezone-agnostic on purpose: they assert the
   * INVARIANT — every boundary is a local midnight — which is trivially true where there is no
   * transition and the first thing to break if the calendar arithmetic ever becomes `+ 7 * 86400000`.
   */

  it('computes a week in calendar days, not in a fixed number of hours', () => {
    stubMatchMedia(true);
    create();

    goTo(new Date(2026, 9, 21));

    const { from, to } = lastRange();

    expect(from.getDay()).toBe(1);
    expect(from.getDate()).toBe(19);
    expect(from.getHours()).toBe(0);

    // Seven calendar days later, and still midnight. Adding 168 hours across the transition would
    // land this an hour out, and every class at the edge of the week would fall in the wrong one.
    expect(to.getDate()).toBe(26);
    expect(to.getMonth()).toBe(9);
    expect(to.getHours()).toBe(0);
  });

  it('measures the grid edges against the day, on a day that is not 24 hours long', () => {
    create();
    host.readOnly.set(false);
    goTo(new Date(2026, 9, 25));
    schedule(10);

    // The last row still ends exactly at the next local midnight, 25 hours after this day began.
    expect(allows(new Date(2026, 9, 25, 23), new Date(2026, 9, 26))).toBe(true);
    expect(allows(new Date(2026, 9, 25, 5, 30), new Date(2026, 9, 25, 6))).toBe(false);
  });

  // --- the hour column ------------------------------------------------------

  it('renders 06:00 to 24:00, in 24-hour Polish time', () => {
    create();

    // Hour starts only: the library renders a label on every segment and hides the half-hour ones in
    // CSS, which a DOM query does not see.
    const hours = [...element().querySelectorAll('.cal-hour-start .cal-time')].map((label) =>
      label.textContent!.trim(),
    );

    // Six to twenty-three inclusive — the 23:00 row is the one that ends at midnight.
    expect(hours.length).toBe(18);
    expect(hours[0]).toBe('06:00');
    expect(hours[hours.length - 1]).toBe('23:00');

    // The library's own formatter would have written "6 AM" here whatever the locale.
    expect(hours.some((hour) => hour.includes('AM') || hour.includes('PM'))).toBe(false);
  });

  // --- drawing --------------------------------------------------------------

  /** The segments of the day on screen, in order. */
  function segments(): HTMLElement[] {
    return [...element().querySelectorAll<HTMLElement>('[data-segment]')];
  }

  /**
   * A primary-pointer press on a segment.
   *
   * jsdom has no `PointerEvent` constructor and no `setPointerCapture`, so both are stood in for: a
   * `MouseEvent` under the pointer type name carries every field the handler reads, and the capture
   * call is stubbed on the element. Neither is what the browser does — what these specs own is the
   * handler's rules, not the pointer machinery.
   */
  function press(segment: HTMLElement): void {
    const captured: number[] = [];
    (segment as unknown as { setPointerCapture: (id: number) => void }).setPointerCapture = (id) =>
      captured.push(id);

    const event = new MouseEvent('pointerdown', { bubbles: true });
    Object.defineProperties(event, {
      isPrimary: { value: true },
      pointerId: { value: 1 },
    });

    segment.dispatchEvent(event);
    fixture.detectChanges();
  }

  function release(): void {
    document.dispatchEvent(new MouseEvent('pointerup'));
    fixture.detectChanges();
  }

  it('previews the class a press would create, and emits it on release', () => {
    create();
    host.readOnly.set(false);
    // Tomorrow, so every segment is ahead of now whatever time the suite runs at.
    goTo(daysFromNow(1));

    press(segments()[0]);

    const draft = element().querySelector('.calendar-draft')!;
    expect(draft).not.toBeNull();
    // The preview says the same thing the overlay will: 06:00, half an hour.
    expect(draft.textContent).toContain('06:00');
    expect(draft.textContent).toContain('06:30');
    expect(host.drawn.length).toBe(0);

    release();

    expect(element().querySelector('.calendar-draft')).toBeNull();
    expect(host.drawn.length).toBe(1);
    expect(host.drawn[0].durationMinutes).toBe(30);
    expect(host.drawn[0].startsAt.getHours()).toBe(6);
  });

  it('refuses a press in the past on the gesture, without opening anything', () => {
    create();
    host.readOnly.set(false);
    goTo(daysFromNow(-1));

    press(segments()[0]);

    // The whole point: the admin is told now, not after filling in a form the API would refuse.
    expect(element().querySelector('.calendar-refusal')).not.toBeNull();
    expect(element().querySelector('.calendar-draft')).toBeNull();

    release();
    expect(host.drawn.length).toBe(0);
  });

  it('draws nothing at all on a read-only calendar', () => {
    create();
    goTo(daysFromNow(1));

    press(segments()[0]);

    // A member never sees a preview, and never sees a refusal either — there is no gesture to refuse.
    expect(element().querySelector('.calendar-draft')).toBeNull();
    expect(element().querySelector('.calendar-refusal')).toBeNull();

    release();
    expect(host.drawn.length).toBe(0);
  });

  // --- the day strip --------------------------------------------------------

  it('navigates the day view by the week it is in', () => {
    create();

    const labels = stripDays().map((day) => day.textContent!.trim());
    expect(labels).toEqual(['Pn', 'Wt', 'Śr', 'Cz', 'Pt', 'So', 'Nd']);

    // The day being read is marked, and it is the one the range starts on.
    const selected = stripDays().findIndex((day) => day.classList.contains('is-selected'));
    expect(selected).toBe((lastRange().from.getDay() + 6) % 7);

    // Friday, in one press rather than four of an arrow — the whole point of the strip.
    stripDays()[4].click();
    fixture.detectChanges();

    expect(lastRange().from.getDay()).toBe(5);
    expect(stripDays()[4].classList).toContain('is-selected');
  });

  it('keeps the weekday when the strip steps a week, and stays a day wide', () => {
    create();

    stripDays()[2].click();
    fixture.detectChanges();

    const wednesday = lastRange().from.getTime();

    click('[aria-label="Następny tydzień"]');

    expect(lastRange().from.getTime()).toBe(wednesday + 7 * 24 * 60 * 60 * 1000);
    expect(stripDays()[2].classList).toContain('is-selected');
  });

  it('sizes every cell alike, arrows included', () => {
    create();

    const cells = [
      ...element().querySelectorAll<HTMLButtonElement>('app-calendar-week-strip .strip-button'),
    ];

    // Seven days between two arrows, all one control: an arrow is not a different kind of thing from
    // a day, so it is not a different kind of button either.
    expect(cells.length).toBe(9);
    expect(cells.every((cell) => cell.classList.contains('strip-button'))).toBe(true);
  });

  // --- rendering ------------------------------------------------------------

  it('renders a class with its time, name, instructor and free spots', () => {
    create();

    const start = lastRange().from;
    host.classes.set([
      at(new Date(start.getFullYear(), start.getMonth(), start.getDate(), 10, 0).toString(), {
        name: 'Pilates',
        instructor: 'Ala',
        freeSpots: 3,
        capacity: 12,
      }),
    ]);
    fixture.detectChanges();

    const tile = element().querySelector('.calendar-tile')!;

    expect(tile.textContent).toContain('Pilates');
    expect(tile.textContent).toContain('Ala');
    expect(tile.textContent).toContain('3 / 12 wolnych');
  });

  it('says a full class is full', () => {
    create();

    const start = lastRange().from;
    host.classes.set([
      at(new Date(start.getFullYear(), start.getMonth(), start.getDate(), 10, 0).toString(), {
        freeSpots: 0,
      }),
    ]);
    fixture.detectChanges();

    expect(element().querySelector('.calendar-tile-full')).not.toBeNull();
  });

  // --- moving and resizing an existing class --------------------------------

  /** Puts one class on the day currently on screen, at the given local hour. */
  function schedule(hour: number, over: Partial<ScheduledClass> = {}): ScheduledClass {
    const day = lastRange().from;
    const row = at(
      new Date(day.getFullYear(), day.getMonth(), day.getDate(), hour, 0).toString(),
      over,
    );

    host.classes.set([row]);
    fixture.detectChanges();

    return row;
  }

  /**
   * Ends a move or a resize the way the library does, without simulating the pointer.
   *
   * jsdom has no layout, so a real drag has no pixels to snap to — what is worth testing here is our
   * handler's rules, not `angular-draggable-droppable`'s arithmetic.
   */
  function finishGesture(newStart: Date, newEnd: Date): void {
    const week = fixture.debugElement.query(By.directive(CalendarWeekViewComponent))
      .componentInstance as CalendarWeekViewComponent;

    week.eventTimesChanged.emit({
      type: CalendarEventTimesChangedEventType.Drag,
      event: week.events[0],
      newStart,
      newEnd,
    });
    fixture.detectChanges();
  }

  it('offers resize handles only where the calendar can be edited', () => {
    create();
    goTo(daysFromNow(1));
    schedule(10);

    expect(element().querySelectorAll('.cal-resize-handle').length).toBe(0);

    host.readOnly.set(false);
    fixture.detectChanges();

    // Both edges: pulling the start earlier and the end later are two ways of saying "longer".
    expect(element().querySelectorAll('.cal-resize-handle').length).toBe(2);
    expect(element().querySelector('.cal-draggable')).not.toBeNull();
  });

  it('offers no gesture on a class that has already started', () => {
    create();
    host.readOnly.set(false);
    // Yesterday: the calendar can show it, but history is not rearrangeable.
    goTo(daysFromNow(-1));
    schedule(10);

    expect(element().querySelectorAll('.cal-resize-handle').length).toBe(0);
    expect(element().querySelector('.cal-draggable')).toBeNull();
  });

  /** The guard the library consults while the pointer moves. */
  function allows(newStart: Date, newEnd: Date): boolean {
    const week = fixture.debugElement.query(By.directive(CalendarWeekViewComponent))
      .componentInstance as CalendarWeekViewComponent;

    return week.validateEventTimesChanged({
      type: CalendarEventTimesChangedEventType.Drag,
      event: week.events[0],
      newStart,
      newEnd,
    });
  }

  it('will not let a class leave the rendered grid', () => {
    create();
    host.readOnly.set(false);
    goTo(daysFromNow(1));
    schedule(10);

    const day = lastRange().from;
    const on = (hour: number, minute = 0) =>
      new Date(day.getFullYear(), day.getMonth(), day.getDate(), hour, minute);

    const midnight = addDays(on(0), 1);

    // Inside: the first slot the grid draws, and the last one — which ends exactly at midnight, the
    // closing edge itself.
    expect(allows(on(6), on(7))).toBe(true);
    expect(allows(on(23), midnight)).toBe(true);

    // Out: above the opening, and over midnight. Refusing during the drag is what stops the block
    // travelling somewhere this view could never show it again.
    expect(allows(on(5, 30), on(6, 30))).toBe(false);
    expect(allows(on(23, 30), new Date(midnight.getTime() + 30 * 60_000))).toBe(false);

    // And a gesture that arrives on the drop anyway writes nothing.
    finishGesture(on(5), on(6));
    expect(host.rescheduled.length).toBe(0);
  });

  it('reports a move as a new start and a new duration', () => {
    create();
    host.readOnly.set(false);
    goTo(daysFromNow(1));
    const row = schedule(10);

    const day = lastRange().from;
    const newStart = new Date(day.getFullYear(), day.getMonth(), day.getDate(), 12, 30);

    finishGesture(newStart, new Date(newStart.getTime() + 45 * 60_000));

    expect(host.rescheduled.length).toBe(1);
    // The class itself rides along, so the screen can send back what the gesture cannot express.
    expect(host.rescheduled[0].class.id).toBe(row.id);
    expect(host.rescheduled[0].startsAt.getHours()).toBe(12);
    expect(host.rescheduled[0].durationMinutes).toBe(45);
  });

  it('refuses a move into the past, and reports nothing', () => {
    create();
    host.readOnly.set(false);
    goTo(daysFromNow(1));
    schedule(10);

    const yesterday = new Date(Date.now() - 24 * 60 * 60 * 1000);

    finishGesture(yesterday, new Date(yesterday.getTime() + 60 * 60_000));

    expect(element().querySelector('.calendar-refusal')).not.toBeNull();
    // Nothing emitted means the screen changes nothing, and the library puts the block back.
    expect(host.rescheduled.length).toBe(0);
  });

  it('ignores a resize that collapses the class below one segment', () => {
    create();
    host.readOnly.set(false);
    goTo(daysFromNow(1));
    schedule(10);

    const day = lastRange().from;
    const start = new Date(day.getFullYear(), day.getMonth(), day.getDate(), 12, 0);

    finishGesture(start, new Date(start.getTime() + 10 * 60_000));

    expect(host.rescheduled.length).toBe(0);
  });

  // --- the three states that must not look alike ----------------------------

  it('shows the empty state only when nothing is loading and nothing failed', () => {
    create();

    expect(element().querySelector('.calendar-empty')).not.toBeNull();

    host.loading.set(true);
    fixture.detectChanges();
    expect(element().querySelector('.calendar-empty')).toBeNull();

    host.loading.set(false);
    host.loadFailed.set(true);
    fixture.detectChanges();
    expect(element().querySelector('.calendar-empty')).toBeNull();
    expect(element().querySelector('.alert')).not.toBeNull();

    host.loadFailed.set(false);
    fixture.detectChanges();
    expect(element().querySelector('.calendar-empty')).not.toBeNull();
  });

  it('keeps the grid mounted through loading and failure, so navigation does not jump', () => {
    create();

    host.loading.set(true);
    fixture.detectChanges();

    expect(element().querySelector('mwl-calendar-week-view')).not.toBeNull();
  });

  // --- projection -----------------------------------------------------------

  it('projects header actions from the screen that has them', () => {
    create();

    expect(element().querySelector('.host-header-action')).not.toBeNull();
  });

  it('renders per-class actions only when the screen is not read-only', () => {
    create();

    const start = lastRange().from;
    host.classes.set([
      at(new Date(start.getFullYear(), start.getMonth(), start.getDate(), 10, 0).toString(), {
        name: 'Pilates',
      }),
    ]);
    fixture.detectChanges();

    expect(element().querySelector('.host-row-action')).toBeNull();

    host.readOnly.set(false);
    fixture.detectChanges();

    // The template receives the real ScheduledClass, not a reconstruction.
    expect(element().querySelector('.host-row-action')!.textContent).toContain('Edytuj Pilates');
  });
});
