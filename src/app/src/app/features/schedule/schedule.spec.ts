import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MyBooking } from '../../core/scheduling/booking.models';
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

  /**
   * Answers every outstanding "my bookings" request with an empty list.
   *
   * Since S-08 a load fetches the week AND the caller's own bookings, in parallel — so every test
   * that triggers a load has two requests to settle, and `controller.verify()` in afterEach is what
   * would otherwise fail. Flushed FIRST, so the order the schedule responses arrive in is what
   * decides which load wins: that is the property the generation-guard test is about.
   */
  function flushMine(rows: MyBooking[] = []): void {
    for (const request of controller.match('/api/bookings/mine')) {
      request.flush(rows);
    }
  }

  /**
   * Drains the microtask queue and renders.
   *
   * TWICE, and that is not superstition: a load is now a `Promise.all` over two requests, so its
   * result lands one microtask turn later than the single fetch this screen used to do. One
   * `whenStable` settles the responses; the second settles the handler that reads them.
   */
  async function settle(): Promise<void> {
    await fixture.whenStable();
    await fixture.whenStable();
    fixture.detectChanges();
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

    flushMine();
    requests[0].flush([]);
  });

  it('refetches with the shifted window when the calendar moves a week', () => {
    const first = scheduleRequests();
    const firstFrom = new Date(first[0].request.params.get('from')!).getTime();
    flushMine();
    first[0].flush([]);

    step('Następny tydzień');

    const second = scheduleRequests();
    const secondFrom = new Date(second[0].request.params.get('from')!).getTime();

    expect(secondFrom).toBe(firstFrom + 7 * 24 * 60 * 60 * 1000);

    flushMine();
    second[0].flush([]);
  });

  it('does not let a stale response overwrite a fresher one', async () => {
    // The race navigation made reachable: nothing cancels an in-flight request, so without the
    // generation guard the LAST RESPONSE would win rather than the last request.
    const first = scheduleRequests();
    flushMine();
    first[0].flush([]);

    step('Następny tydzień');
    const second = scheduleRequests()[0];

    step('Następny tydzień');
    const third = scheduleRequests()[0];

    // The bookings half of both loads settles first, so what remains is purely the order the two
    // schedule responses arrive in — which is what this test is about.
    flushMine();

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
    flushMine();
    scheduleRequests()[0].flush('boom', { status: 500, statusText: 'Server Error' });
    await settle();

    const html = fixture.nativeElement as HTMLElement;

    expect(html.querySelector('.alert')).not.toBeNull();
    expect(html.querySelector('.calendar-empty')).toBeNull();
  });

  it('offers a retry that refetches the window on screen', async () => {
    const first = scheduleRequests()[0];
    const window = first.request.params.get('from');

    flushMine();
    first.flush('boom', { status: 500, statusText: 'Server Error' });
    await settle();

    const html = fixture.nativeElement as HTMLElement;
    const retry = Array.from(html.querySelectorAll<HTMLButtonElement>('.alert .link-button')).find(
      (button) => (button.textContent ?? '').includes('Spróbuj ponownie'),
    )!;

    // Without this the member's only recovery from a dropped connection is a page reload.
    expect(retry).not.toBeUndefined();

    retry.click();
    await fixture.whenStable();
    fixture.detectChanges();

    const again = await vi.waitFor(() => scheduleRequests()[0]);

    // The window ON SCREEN, not a reset to today: the member may have navigated before it failed.
    expect(again.request.params.get('from')).toBe(window);

    flushMine();
    again.flush([]);
    await settle();

    expect(html.querySelector('.alert')).toBeNull();
  });

  // --- S-08: booking from the schedule --------------------------------------

  /** Opens the detail overlay for the only class on screen. */
  async function openFirstTile(): Promise<HTMLElement> {
    const html = fixture.nativeElement as HTMLElement;

    html.querySelector<HTMLButtonElement>('.calendar-tile-button')!.click();
    await settle();

    return html;
  }

  function tile(): ScheduledClass {
    const start = new Date();

    return at(new Date(start.getFullYear(), start.getMonth(), start.getDate(), 23, 30).toString(), {
      id: 'c1',
      freeSpots: 4,
      capacity: 12,
    });
  }

  it('applies a booking in place, without refetching the week', async () => {
    flushMine();
    scheduleRequests()[0].flush([tile()]);
    await settle();

    const html = await openFirstTile();

    [...html.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('Zapisz się'))!
      .click();
    fixture.detectChanges();

    // The server answers with the class as it now stands - one fewer spot.
    controller.expectOne('/api/classes/c1/bookings').flush({ ...tile(), freeSpots: 3 });
    await fixture.whenStable();
    fixture.detectChanges();

    // Replaced in place. A refetch of either endpoint here would mean the response was thrown away.
    controller.expectNone('/api/bookings/mine');
    expect(scheduleRequests().length).toBe(0);

    expect(html.querySelector('.calendar-tile')!.textContent).toContain('3 / 12 wolnych');
    // The overlay stays open and now offers the cancel, which is the member's only confirmation.
    expect(html.textContent).toContain('Jesteś zapisany');
  });

  it('shows a refusal inside the overlay and leaves the tile alone', async () => {
    flushMine();
    scheduleRequests()[0].flush([tile()]);
    await settle();

    const html = await openFirstTile();

    [...html.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('Zapisz się'))!
      .click();
    fixture.detectChanges();

    controller
      .expectOne('/api/classes/c1/bookings')
      .flush({ reason: 'class_full' }, { status: 409, statusText: 'Conflict' });
    await settle();

    // In the overlay, not as a screen-level banner: a banner above a calendar reads as being about
    // the week rather than about the class the member tapped.
    expect(html.querySelector('.overlay-panel .alert')!.textContent).toContain(
      'Brak wolnych miejsc',
    );
    expect(html.querySelector('.calendar-tile')!.textContent).toContain('4 / 12 wolnych');
  });

  it('knows which classes the member already holds', async () => {
    flushMine([
      {
        bookingId: 'b1',
        classId: 'c1',
        name: 'Joga',
        description: null,
        startsAt: tile().startsAt,
        durationMinutes: 60,
        instructor: 'Ola',
        bookedAt: new Date().toISOString(),
      },
    ]);
    scheduleRequests()[0].flush([tile()]);
    await settle();

    const html = await openFirstTile();

    // Resolved from the member's own bookings rather than from a field on ScheduledClass - the
    // shared projection deliberately carries no bookedByMe.
    expect(html.textContent).toContain('Jesteś zapisany');
  });

  it('does not stay busy when the week changes mid-booking', async () => {
    flushMine();
    scheduleRequests()[0].flush([tile()]);
    await settle();

    const html = await openFirstTile();

    [...html.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('Zapisz się'))!
      .click();
    fixture.detectChanges();

    // Navigating away mid-flight bumps the generation, so the RESULT is rightly discarded. The busy
    // flag must not be discarded with it: `[busy]="acting()"` disables the overlay's own buttons, so
    // an acting() stuck true leaves every later overlay dead until the page is reloaded.
    step('Następny tydzień');

    controller.expectOne('/api/classes/c1/bookings').flush({ ...tile(), freeSpots: 3 });
    flushMine();
    scheduleRequests()[0].flush([]);
    await settle();

    // Back to the week the class is on, so there is a tile to reopen.
    step('Poprzedni tydzień');
    flushMine();
    scheduleRequests()[0].flush([tile()]);
    await settle();

    const reopened = await openFirstTile();
    const book = [...reopened.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Zapisz się'),
    )!;

    expect(book.disabled).toBe(false);
  });
});
