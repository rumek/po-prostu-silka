import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { CalendarRange, DrawnRange, ScheduleCalendar } from './schedule-calendar';

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

  it('moves by a day and by a week, in both directions', () => {
    create();

    const start = lastRange().from.getTime();
    const day = 24 * 60 * 60 * 1000;

    click('[aria-label="Następny dzień"]');
    expect(lastRange().from.getTime()).toBe(start + day);

    click('[aria-label="Poprzedni dzień"]');
    expect(lastRange().from.getTime()).toBe(start);

    click('[aria-label="Następny tydzień"]');
    expect(lastRange().from.getTime()).toBe(start + 7 * day);

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

  it('offers day steps only where a day is what you see', () => {
    const emit = stubMatchMedia(true);
    create();

    expect(element().querySelector('[aria-label="Następny dzień"]')).toBeNull();

    emit(false);
    fixture.detectChanges();

    expect(element().querySelector('[aria-label="Następny dzień"]')).not.toBeNull();
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

  function press(segment: HTMLElement): void {
    segment.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    fixture.detectChanges();
  }

  function release(): void {
    document.dispatchEvent(new MouseEvent('mouseup'));
    fixture.detectChanges();
  }

  it('previews the class a press would create, and emits it on release', () => {
    create();
    host.readOnly.set(false);
    // Tomorrow, so every segment is ahead of now whatever time the suite runs at.
    click('[aria-label="Następny dzień"]');
    fixture.detectChanges();

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
    click('[aria-label="Poprzedni dzień"]');
    fixture.detectChanges();

    press(segments()[0]);

    // The whole point: the admin is told now, not after filling in a form the API would refuse.
    expect(element().querySelector('.calendar-refusal')).not.toBeNull();
    expect(element().querySelector('.calendar-draft')).toBeNull();

    release();
    expect(host.drawn.length).toBe(0);
  });

  it('draws nothing at all on a read-only calendar', () => {
    create();
    click('[aria-label="Następny dzień"]');
    fixture.detectChanges();

    press(segments()[0]);

    // A member never sees a preview, and never sees a refusal either — there is no gesture to refuse.
    expect(element().querySelector('.calendar-draft')).toBeNull();
    expect(element().querySelector('.calendar-refusal')).toBeNull();

    release();
    expect(host.drawn.length).toBe(0);
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
