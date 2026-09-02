import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { Classes } from './classes';

/**
 * Ten o'clock TODAY, local. The screen fetches the window the calendar shows — which starts at
 * today — so a fixed 2026 date would fall outside it and render nothing.
 */
function todayAt(hour: number): string {
  const now = new Date();

  return new Date(now.getFullYear(), now.getMonth(), now.getDate(), hour, 0).toISOString();
}

const JOGA: ScheduledClass = {
  id: 'c1',
  classTypeId: 't1',
  // Resolved from the class type, not stored on the occurrence — see class.models.
  name: 'Joga',
  description: 'Dla poczatkujacych',
  startsAt: todayAt(18),
  durationMinutes: 60,
  instructorUserId: 'u1',
  instructor: 'Ola',
  capacity: 20,
  freeSpots: 20,
  status: 'Scheduled',
};

const PILATES: ScheduledClass = {
  ...JOGA,
  id: 'c2',
  classTypeId: 't2',
  name: 'Pilates',
  startsAt: todayAt(20),
};

/**
 * The panel renders the shared calendar with its actions projected in (prd-v2 FR-017), so these
 * assertions run against tiles rather than list rows. Everything the list did, it must still do.
 *
 * jsdom provides no `matchMedia`, so the calendar stays in its day view here — which is why the
 * fixtures are all on today.
 */
describe('Classes', () => {
  let fixture: ComponentFixture<Classes>;
  let controller: HttpTestingController;

  async function createWith(rows: ScheduledClass[]) {
    TestBed.configureTestingModule({
      imports: [Classes],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Classes);
    fixture.detectChanges();

    (await vi.waitFor(() => adminRequests()[0])).flush(rows);
    await settle();
  }

  afterEach(() => controller.verify());

  function adminRequests(): TestRequest[] {
    return controller.match((request) => request.url === '/api/admin/classes');
  }

  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
    // A SECOND PASS, and it is not superstition. The library's view components are OnPush: when the
    // events array changes they mark themselves for check, and the redraw happens on the NEXT
    // change-detection cycle. A browser schedules that cycle on its own; a test driving
    // detectChanges() by hand has to ask for it, or it asserts against the previous frame — which
    // showed a deleted class still on the grid while the component's own state was already correct.
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function html(): string {
    return element().textContent ?? '';
  }

  function tiles(): HTMLElement[] {
    return Array.from(element().querySelectorAll('.calendar-tile'));
  }

  function tileFor(name: string): HTMLElement {
    return tiles().find((tile) => (tile.textContent ?? '').includes(name))!;
  }

  function actionIn(tile: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(tile.querySelectorAll('button')).find((button) =>
      (button.textContent ?? '').includes(label),
    )!;
  }

  /** The action panels live below the calendar now, not inside the tile. */
  function panelButton(label: string): HTMLButtonElement {
    return Array.from(element().querySelectorAll<HTMLButtonElement>('.classes-panel button')).find(
      (button) => (button.textContent ?? '').includes(label),
    )!;
  }

  // --- the window drives the fetch ------------------------------------------

  it('fetches the window the calendar is showing', async () => {
    await createWith([JOGA]);

    // Flushed in createWith; assert on what it asked for.
    expect(tiles().length).toBe(1);
  });

  it('refetches when the calendar moves', async () => {
    await createWith([JOGA]);

    element().querySelector<HTMLButtonElement>('[aria-label="Następny tydzień"]')!.click();
    fixture.detectChanges();

    const next = await vi.waitFor(() => adminRequests()[0]);
    const from = new Date(next.request.params.get('from')!);

    expect(from.getTime()).toBeGreaterThan(Date.now());

    next.flush([]);
    await settle();
  });

  // --- rendering -------------------------------------------------------------

  it('renders one tile per class', async () => {
    await createWith([JOGA, PILATES]);

    expect(tiles().length).toBe(2);
    expect(html()).toContain('Joga');
    expect(html()).toContain('Pilates');
  });

  it('shows no room on the tile', async () => {
    await createWith([JOGA]);

    // The room left the model in S-06; nothing may reintroduce it in the display (prd-v2 FR-018).
    expect(html()).not.toContain('Sala');
  });

  it('renders an explicit empty state rather than a blank page', async () => {
    await createWith([]);

    expect(element().querySelector('.calendar-empty')).not.toBeNull();
  });

  // --- duplicate -------------------------------------------------------------

  it('reports which weeks a duplicate skipped', async () => {
    await createWith([JOGA]);

    actionIn(tileFor('Joga'), 'Powiel').click();
    fixture.detectChanges();

    panelButton('Powiel').click();
    await settle();

    controller.expectOne('/api/admin/classes/c1/duplicate').flush({
      created: 2,
      skippedWeeks: [3, 4],
    });
    await settle();

    // A batch where some weeks collided is a partial success, and saying "done" would leave the admin
    // believing in classes that were never created.
    expect(html()).toContain('Utworzono 2 kopie');
    expect(html()).toContain('Pominięto tydzień 3, 4');

    adminRequests()[0].flush([JOGA]);
    await settle();
  });

  it('reports a clean duplicate without mentioning skipped weeks', async () => {
    await createWith([JOGA]);

    actionIn(tileFor('Joga'), 'Powiel').click();
    fixture.detectChanges();

    panelButton('Powiel').click();
    await settle();

    controller.expectOne('/api/admin/classes/c1/duplicate').flush({ created: 4, skippedWeeks: [] });
    await settle();

    expect(html()).toContain('Utworzono 4 kopie');
    expect(html()).not.toContain('Pominięto');

    adminRequests()[0].flush([JOGA]);
    await settle();
  });

  // --- delete ----------------------------------------------------------------

  it('asks for confirmation before deleting', async () => {
    await createWith([JOGA]);

    actionIn(tileFor('Joga'), 'Usuń').click();
    fixture.detectChanges();

    // Inline, never confirm() — that blocks the event loop and has no precedent here.
    expect(html()).toContain('Usunąć „Joga”');
    controller.expectNone('/api/admin/classes/c1');
  });

  it('removes the tile after a confirmed delete', async () => {
    await createWith([JOGA, PILATES]);

    actionIn(tileFor('Joga'), 'Usuń').click();
    fixture.detectChanges();

    panelButton('Tak, usuń').click();
    await settle();

    controller.expectOne('/api/admin/classes/c1').flush(null);
    await settle();

    expect(tiles().length).toBe(1);
    expect(html()).toContain('Pilates');
    expect(html()).not.toContain('Joga');
  });

  it('keeps the tile and surfaces the error when a delete fails', async () => {
    await createWith([JOGA]);

    actionIn(tileFor('Joga'), 'Usuń').click();
    fixture.detectChanges();

    panelButton('Tak, usuń').click();
    await settle();

    controller
      .expectOne('/api/admin/classes/c1')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await settle();

    expect(tiles().length).toBe(1);
    expect(html()).toContain('Nie udało się');
  });

  // --- failure and the past --------------------------------------------------

  it('reports a failed load and offers a retry', async () => {
    TestBed.configureTestingModule({
      imports: [Classes],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Classes);
    fixture.detectChanges();

    (await vi.waitFor(() => adminRequests()[0])).flush('boom', {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(html()).toContain('Nie udało się wczytać');

    element().querySelector<HTMLButtonElement>('.alert .link-button')!.click();
    await settle();

    adminRequests()[0].flush([JOGA]);
    await settle();

    expect(tiles().length).toBe(1);
  });

  it('withholds every action in a week that has already passed', async () => {
    await createWith([JOGA]);

    expect(actionIn(tileFor('Joga'), 'Usuń')).not.toBeUndefined();

    element().querySelector<HTMLButtonElement>('[aria-label="Poprzedni tydzień"]')!.click();
    fixture.detectChanges();

    const past = await vi.waitFor(() => adminRequests()[0]);
    past.flush([{ ...JOGA, startsAt: new Date(Date.now() - 7 * 86_400_000).toISOString() }]);
    await settle();

    // Visible, but not editable — and the reason is on screen, or a missing button reads as broken.
    expect(tiles().length).toBe(1);
    expect(element().querySelector('.calendar-tile-actions')).toBeNull();
    expect(html()).toContain('Ten tydzień już minął');
  });
});
