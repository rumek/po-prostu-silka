import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MyBooking } from '../../core/scheduling/booking.models';
import { MyClasses } from './my-classes';

function booking(over: Partial<MyBooking> = {}): MyBooking {
  return {
    bookingId: over.bookingId ?? 'b1',
    classId: over.classId ?? 'c1',
    name: over.name ?? 'Joga',
    description: over.description ?? null,
    startsAt: over.startsAt ?? new Date(Date.now() + 86_400_000).toISOString(),
    durationMinutes: over.durationMinutes ?? 60,
    instructor: over.instructor ?? 'Ola',
    bookedAt: over.bookedAt ?? new Date().toISOString(),
  };
}

/**
 * "Moje zajęcia" (prd.md FR-010).
 *
 * The tri-state and the per-row cancel are what these tests protect. The ORDER is deliberately not
 * asserted: the server orders by the class's start, and re-sorting here would be a second source of
 * truth for the same rule.
 */
describe('MyClasses', () => {
  let fixture: ComponentFixture<MyClasses>;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MyClasses],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyClasses);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function respond(rows: MyBooking[]): Promise<void> {
    controller.expectOne('/api/bookings/mine').flush(rows);
    // The load is async, so the signal is not written until the microtask queue drains - detecting
    // changes before that renders the state the screen was in a moment ago.
    await settle();
  }

  /** Lets a pending handler run before anything is asserted. */
  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function rows(): HTMLElement[] {
    return [...element().querySelectorAll<HTMLElement>('.my-classes-row')];
  }

  it('loads on init and renders each booking with its class, time and instructor', async () => {
    await respond([booking({ name: 'Pilates', instructor: 'Ala' })]);

    expect(rows().length).toBe(1);
    expect(rows()[0].textContent).toContain('Pilates');
    expect(rows()[0].textContent).toContain('Ala');
  });

  it('shows an empty state that leads to the schedule', async () => {
    await respond([]);

    // "Nothing here" with no next step is the version of this screen every new member sees first.
    expect(element().textContent).toContain('Nie masz jeszcze żadnych zapisów');
    expect(element().querySelector('a[href="/schedule"]')).not.toBeNull();
  });

  it('offers a retry when the load fails', async () => {
    controller.expectOne('/api/bookings/mine').error(new ProgressEvent('failed'));
    await settle();

    expect(element().querySelector('[role="alert"]')).not.toBeNull();

    element().querySelector<HTMLButtonElement>('.link-button')!.click();
    fixture.detectChanges();

    await respond([booking()]);

    expect(rows().length).toBe(1);
  });

  it('cancels by CLASS and removes the row in place', async () => {
    await respond([
      booking({ bookingId: 'b1', classId: 'c1' }),
      booking({ bookingId: 'b2', classId: 'c2' }),
    ]);

    rows()[0].querySelector<HTMLButtonElement>('button')!.click();
    fixture.detectChanges();

    // Addressed by class, not by booking id: a member holds at most one active booking per class.
    const request = controller.expectOne('/api/classes/c1/bookings/mine');
    expect(request.request.method).toBe('DELETE');

    // The server answers with the class; this screen has no use for it beyond knowing it worked.
    request.flush({});
    await settle();

    // Removed rather than refetched — the response already said the spot is gone.
    expect(rows().length).toBe(1);
    expect(rows()[0].textContent).toContain('Joga');
    controller.expectNone('/api/bookings/mine');
  });

  it('keeps the row and explains the refusal when a cancellation is rejected', async () => {
    await respond([booking({ bookingId: 'b1', classId: 'c1' })]);

    rows()[0].querySelector<HTMLButtonElement>('button')!.click();
    fixture.detectChanges();

    controller
      .expectOne('/api/classes/c1/bookings/mine')
      .flush({ reason: 'not_booked' }, { status: 409, statusText: 'Conflict' });
    await settle();

    // The row survives a refusal: removing it would tell the member the spot was released when it
    // was not.
    expect(rows().length).toBe(1);
    expect(element().querySelector('.field-error')!.textContent).toContain('Nie jesteś zapisany');
  });
});
