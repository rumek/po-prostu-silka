import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { Schedule } from './schedule';

/** Builds a class starting at a LOCAL wall-clock time, expressed as the UTC instant the API sends. */
function at(local: string, over: Partial<ScheduledClass> = {}): ScheduledClass {
  return {
    id: over.id ?? local,
    name: over.name ?? 'Joga',
    startsAt: new Date(local).toISOString(),
    durationMinutes: over.durationMinutes ?? 60,
    room: over.room ?? 'Sala A',
    instructor: over.instructor ?? 'Ola',
    capacity: over.capacity ?? 20,
    freeSpots: over.freeSpots ?? 20,
    status: 'Scheduled',
  };
}

describe('Schedule', () => {
  let fixture: ComponentFixture<Schedule>;
  let controller: HttpTestingController;

  async function createWith(rows: ScheduledClass[]) {
    TestBed.configureTestingModule({
      imports: [Schedule],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Schedule);

    (await vi.waitFor(() => controller.expectOne('/api/classes'))).flush(rows);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => controller.verify());

  function html(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function days(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.schedule-day'));
  }

  function rows(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.schedule-row'));
  }

  it('groups classes into one section per day', async () => {
    await createWith([
      at('2026-09-04T10:00', { id: 'a' }),
      at('2026-09-04T18:00', { id: 'b' }),
      at('2026-09-05T10:00', { id: 'c' }),
    ]);

    expect(days().length).toBe(2);
    expect(rows().length).toBe(3);
  });

  /**
   * The grouping key must be the LOCAL calendar date. A UTC-based key would push a late-evening
   * class into the next day for any zone ahead of UTC — the club's own zone included.
   */
  it('keeps a late-evening class on its own local day', async () => {
    await createWith([
      at('2026-09-04T23:30', { id: 'late' }),
      at('2026-09-05T08:00', { id: 'am' }),
    ]);

    expect(days().length).toBe(2);
    expect(rows()[0].textContent).toContain('23:30');
  });

  it('renders name, room, instructor and free spots', async () => {
    await createWith([
      at('2026-09-04T18:00', { name: 'Pilates', room: 'Sala B', instructor: 'Ewa', freeSpots: 7 }),
    ]);

    expect(html()).toContain('Pilates');
    expect(html()).toContain('Sala B');
    expect(html()).toContain('Ewa');
    expect(html()).toContain('7');
  });

  it('shows the end time derived from the duration', async () => {
    await createWith([at('2026-09-04T18:00', { durationMinutes: 90 })]);

    expect(rows()[0].textContent).toContain('18:00');
    expect(rows()[0].textContent).toContain('19:30');
  });

  /**
   * freeSpots is read as its own field, never assumed equal to capacity — S-04 makes them differ by
   * changing only the server projection.
   */
  it('says a class is full when no spots remain', async () => {
    await createWith([at('2026-09-04T18:00', { freeSpots: 0, capacity: 20 })]);

    expect(html()).toContain('Brak miejsc');
  });

  it('renders an explicit empty state rather than a blank page', async () => {
    await createWith([]);

    expect(days().length).toBe(0);
    expect(html()).toContain('Brak zajęć');
  });

  it('reports a failed load and offers a retry', async () => {
    TestBed.configureTestingModule({
      imports: [Schedule],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Schedule);

    (await vi.waitFor(() => controller.expectOne('/api/classes'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(html()).toContain('Nie udało się wczytać');

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.link-button')!
      .click();

    (await vi.waitFor(() => controller.expectOne('/api/classes'))).flush([at('2026-09-04T18:00')]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(rows().length).toBe(1);
  });
});
