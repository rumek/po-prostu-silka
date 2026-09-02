import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ClassTypeSummary } from '../../../core/scheduling/class-type.models';
import { DrawnRange } from '../../../shared/calendar/schedule-calendar';
import { ClassCreateOverlay } from './class-create-overlay';

const TYPE: ClassTypeSummary = {
  id: 't1',
  name: 'Joga',
  description: 'Dla poczatkujacych',
  defaultDurationMinutes: 60,
  defaultCapacity: 12,
  isActive: true,
  createdAt: '2026-09-01T10:00:00Z',
};

const RETIRED: ClassTypeSummary = { ...TYPE, id: 't2', name: 'Stare', isActive: false };

/** 45 minutes drawn from 10:00 — a duration no type default would produce, so prefill is provable. */
const DRAWN: DrawnRange = {
  startsAt: new Date(2030, 5, 3, 10, 0),
  durationMinutes: 45,
};

@Component({
  imports: [ClassCreateOverlay],
  template: `
    <app-class-create-overlay
      [drawn]="drawn()"
      (created)="createdCount = createdCount + 1"
      (closed)="closedCount = closedCount + 1"
    />
  `,
})
class Host {
  readonly drawn = signal<DrawnRange>(DRAWN);
  createdCount = 0;
  closedCount = 0;
}

/**
 * The overlay is tested from its inputs down: what it prefills, what it submits, and what it says
 * when refused. The drag gesture that produces `drawn` is NOT simulated — synthesising pointer
 * events in jsdom tests the synthesis, not the user; that path is covered manually.
 */
describe('ClassCreateOverlay', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;
  let controller: HttpTestingController;

  async function create(
    types: ClassTypeSummary[] = [TYPE],
    trainers = [{ id: 'u1', displayName: 'Ola' }],
  ) {
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    fixture.detectChanges();

    (await vi.waitFor(() => controller.expectOne('/api/admin/class-types'))).flush(types);
    controller.expectOne('/api/admin/trainers').flush(trainers);
    await settle();
  }

  afterEach(() => controller.verify());

  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
    // A second pass, for the same reason classes.spec.ts documents: the awaited work resolves in a
    // microtask after whenStable() returns, so a single hand-driven detectChanges() renders the frame
    // before the data landed. A browser schedules the follow-up cycle itself.
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function html(): string {
    return element().textContent ?? '';
  }

  function select(name: string): HTMLSelectElement {
    return element().querySelector<HTMLSelectElement>(`select[name="${name}"]`)!;
  }

  function number(name: string): HTMLInputElement {
    return element().querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
  }

  async function choose(name: string, value: string) {
    const control = select(name);
    control.value = value;
    control.dispatchEvent(new Event('change'));
    await settle();
  }

  function button(label: string): HTMLButtonElement {
    return Array.from(element().querySelectorAll('button')).find((candidate) =>
      (candidate.textContent ?? '').includes(label),
    )!;
  }

  // --- prefill ---------------------------------------------------------------

  it('prefills the duration the gesture drew', async () => {
    await create();

    expect(number('durationMinutes').value).toBe('45');
    expect(html()).toContain('45 min');
  });

  it('copies the capacity from the chosen type but keeps the drawn duration', async () => {
    await create();

    await choose('classTypeId', 't1');

    // Capacity has no gesture, so the type's default wins.
    expect(number('capacity').value).toBe('12');
    // Duration does — the admin just expressed 45, and the type's 60 must not overwrite it.
    expect(number('durationMinutes').value).toBe('45');
  });

  it('offers only active types', async () => {
    await create([TYPE, RETIRED]);

    // getAll() is unfiltered by design; a retired type must not be newly attachable (prd-v2 FR-006).
    expect(html()).toContain('Joga');
    expect(html()).not.toContain('Stare');
  });

  // --- submitting ------------------------------------------------------------

  it('submits the drawn start with the chosen type, trainer and numbers', async () => {
    await create();

    await choose('classTypeId', 't1');
    await choose('instructorUserId', 'u1');

    button('Dodaj zajęcia').click();
    await settle();

    const request = controller.expectOne('/api/admin/classes');

    expect(request.request.body).toEqual({
      classTypeId: 't1',
      startsAt: DRAWN.startsAt.toISOString(),
      durationMinutes: 45,
      instructorUserId: 'u1',
      capacity: 12,
    });

    request.flush({});
    await settle();

    expect(host.createdCount).toBe(1);
  });

  it('will not submit before a type and a trainer are chosen', async () => {
    await create();

    expect(button('Dodaj zajęcia').disabled).toBe(true);

    await choose('classTypeId', 't1');
    expect(button('Dodaj zajęcia').disabled).toBe(true);

    await choose('instructorUserId', 'u1');
    expect(button('Dodaj zajęcia').disabled).toBe(false);
  });

  // --- refusals --------------------------------------------------------------

  it('keeps the overlay open with its values when the slot is taken', async () => {
    await create();

    await choose('classTypeId', 't1');
    await choose('instructorUserId', 'u1');

    button('Dodaj zajęcia').click();
    await settle();

    controller
      .expectOne('/api/admin/classes')
      .flush({ reason: 'time_conflict' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(html()).toContain('inne zajęcia');
    expect(host.createdCount).toBe(0);
    expect(host.closedCount).toBe(0);
    // The values survive, or the admin retypes everything to try one minute later.
    expect(select('classTypeId').value).toBe('t1');
    expect(number('durationMinutes').value).toBe('45');
  });

  it('reports a stale trainer selection the way the class form does', async () => {
    await create();

    await choose('classTypeId', 't1');
    await choose('instructorUserId', 'u1');

    button('Dodaj zajęcia').click();
    await settle();

    controller
      .expectOne('/api/admin/classes')
      .flush({ reason: 'instructor_not_trainer' }, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(html()).toContain('aktywnych trenerów');
  });

  // --- nothing to pick -------------------------------------------------------

  it('does not offer a submit that cannot succeed when there are no trainers', async () => {
    await create([TYPE], []);

    expect(html()).toContain('rolą trenera');
    expect(button('Dodaj zajęcia')).toBeUndefined();
  });

  it('does not offer a submit that cannot succeed when every type is retired', async () => {
    await create([RETIRED]);

    expect(html()).toContain('typ zajęć');
    expect(button('Dodaj zajęcia')).toBeUndefined();
  });

  // --- closing ---------------------------------------------------------------

  it('closes without writing', async () => {
    await create();

    button('Anuluj').click();
    await settle();

    expect(host.closedCount).toBe(1);
    controller.expectNone('/api/admin/classes');
  });
});
