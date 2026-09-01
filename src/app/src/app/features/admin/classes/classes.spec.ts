import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { Classes } from './classes';

const JOGA: ScheduledClass = {
  id: 'c1',
  name: 'Joga',
  startsAt: new Date('2026-09-04T18:00').toISOString(),
  durationMinutes: 60,
  room: 'Sala A',
  instructor: 'Ola',
  capacity: 20,
  freeSpots: 20,
  status: 'Scheduled',
};

const PILATES: ScheduledClass = { ...JOGA, id: 'c2', name: 'Pilates', room: 'Sala B' };

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

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush(rows);
    await settle();
  }

  afterEach(() => controller.verify());

  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function html(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function rows(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.classes-row'));
  }

  function buttonIn(row: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(row.querySelectorAll('button')).find((b) =>
      (b.textContent ?? '').includes(label),
    )!;
  }

  /**
   * The confirm button inside an opened panel. Needed because the row's own "Powiel" toggle and the
   * panel's "Powiel" submit share a label — matching on text alone finds the toggle and closes the
   * panel instead of submitting.
   */
  function panelButton(row: HTMLElement, label: string): HTMLButtonElement {
    return Array.from(row.querySelectorAll<HTMLButtonElement>('.classes-panel button')).find((b) =>
      (b.textContent ?? '').includes(label),
    )!;
  }

  it('renders one row per class', async () => {
    await createWith([JOGA, PILATES]);

    expect(rows().length).toBe(2);
    expect(html()).toContain('Joga');
    expect(html()).toContain('Sala B');
  });

  it('renders an explicit empty state rather than a blank page', async () => {
    await createWith([]);

    expect(rows().length).toBe(0);
    expect(html()).toContain('Brak zaplanowanych');
  });

  /**
   * The endpoint's partial-success contract exists so this can be reported. Showing a bare "done"
   * would leave the admin believing in classes that were never created.
   */
  it('reports which weeks a duplicate skipped', async () => {
    await createWith([JOGA]);

    buttonIn(rows()[0], 'Powiel').click();
    await settle();

    panelButton(rows()[0], 'Powiel').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes/c1/duplicate'))).flush({
      created: 7,
      skippedWeeks: [3],
    });
    await settle();

    // The refetch the component issues after a successful duplicate.
    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush([JOGA]);
    await settle();

    expect(html()).toContain('7');
    expect(html()).toContain('Pominięto tydzień 3');
  });

  it('reports a clean duplicate without mentioning skipped weeks', async () => {
    await createWith([JOGA]);

    buttonIn(rows()[0], 'Powiel').click();
    await settle();
    panelButton(rows()[0], 'Powiel').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes/c1/duplicate'))).flush({
      created: 4,
      skippedWeeks: [],
    });
    await settle();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush([JOGA]);
    await settle();

    expect(html()).toContain('Utworzono 4');
    expect(html()).not.toContain('Pominięto');
  });

  /** Deleting is irreversible and there is no cancel until S-05, so it must not be one click. */
  it('asks for confirmation before deleting', async () => {
    await createWith([JOGA]);

    buttonIn(rows()[0], 'Usuń').click();
    await settle();

    expect(html()).toContain('na dobre');

    // afterEach's controller.verify() proves no DELETE was issued by merely opening the prompt.
  });

  it('removes the row after a confirmed delete', async () => {
    await createWith([JOGA, PILATES]);

    buttonIn(rows()[0], 'Usuń').click();
    await settle();

    panelButton(rows()[0], 'Tak, usuń').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes/c1'))).flush(null);
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).not.toContain('Joga');
  });

  it('keeps the row and surfaces the error when a delete fails', async () => {
    await createWith([JOGA]);

    buttonIn(rows()[0], 'Usuń').click();
    await settle();
    panelButton(rows()[0], 'Tak, usuń').click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes/c1'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(rows().length).toBe(1);
    expect(html()).toContain('Nie udało się');
  });

  it('reports a failed load and offers a retry', async () => {
    TestBed.configureTestingModule({
      imports: [Classes],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Classes);

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(html()).toContain('Nie udało się wczytać');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();

    (await vi.waitFor(() => controller.expectOne('/api/admin/classes'))).flush([JOGA]);
    await settle();

    expect(rows().length).toBe(1);
  });
});
