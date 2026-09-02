import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { Schedule } from './schedule';

/** Builds a class starting at a LOCAL wall-clock time, expressed as the UTC instant the API sends. */
function at(local: string, over: Partial<ScheduledClass> = {}): ScheduledClass {
  return {
    id: over.id ?? local,
    classTypeId: over.classTypeId ?? 't1',
    // name, description and instructor arrive RESOLVED from the type and the trainer's account —
    // the occurrence carries none of the three (prd-v2 FR-007, FR-009, FR-010).
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
 * The screen since S-07 is a data shell around the shared calendar: the calendar says which window it
 * is showing, this fetches it. So these tests are about REQUESTS — the window asked for, and which
 * response is allowed to win — while the rendering they used to assert now lives in
 * shared/calendar/schedule-calendar.spec.ts.
 *
 * jsdom provides no `matchMedia`, so the calendar stays in its day-first default here. That is
 * deliberate: it is the same shape as a phone, which is the case this screen is designed around.
 */
describe('Schedule', () => {
  let fixture: ComponentFixture<Schedule>;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Schedule],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Schedule);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  function scheduleRequests(): TestRequest[] {
    return controller.match((request) => request.url === '/api/classes');
  }

  function step(label: string): void {
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>(`[aria-label="${label}"]`)!
      .click();
    fixture.detectChanges();
  }

  it('fetches the visible window on first render, without an ngOnInit of its own', () => {
    const requests = scheduleRequests();

    expect(requests.length).toBe(1);

    const from = new Date(requests[0].request.params.get('from')!);
    const to = new Date(requests[0].request.params.get('to')!);

    // A day, starting at local midnight — the calendar's default view.
    expect(from.getHours()).toBe(0);
    expect(to.getTime() - from.getTime()).toBe(24 * 60 * 60 * 1000);

    requests[0].flush([]);
  });

  it('refetches with the shifted window when the calendar moves a week', () => {
    const first = scheduleRequests();
    const firstFrom = new Date(first[0].request.params.get('from')!).getTime();
    first[0].flush([]);

    step('Następny tydzień');

    const second = scheduleRequests();
    const secondFrom = new Date(second[0].request.params.get('from')!).getTime();

    expect(secondFrom).toBe(firstFrom + 7 * 24 * 60 * 60 * 1000);

    second[0].flush([]);
  });

  it('does not let a stale response overwrite a fresher one', async () => {
    // The race navigation made reachable: nothing cancels an in-flight request, so without the
    // generation guard the LAST RESPONSE would win rather than the last request.
    const first = scheduleRequests();
    first[0].flush([]);

    step('Następny tydzień');
    const second = scheduleRequests()[0];

    step('Następny tydzień');
    const third = scheduleRequests()[0];

    // Third answers first, then the stale second arrives.
    third.flush([at('2026-09-18T10:00', { id: 'fresh' })]);
    await fixture.whenStable();

    second.flush([at('2026-09-11T10:00', { id: 'stale' })]);
    await fixture.whenStable();
    fixture.detectChanges();

    const rows = (fixture.componentInstance as unknown as { rows: () => ScheduledClass[] }).rows();

    expect(rows.map((row) => row.id)).toEqual(['fresh']);
  });

  it('surfaces a failed load without pretending the window is empty', async () => {
    scheduleRequests()[0].flush('boom', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;

    expect(html.querySelector('.alert')).not.toBeNull();
    expect(html.querySelector('.calendar-empty')).toBeNull();
  });
});
