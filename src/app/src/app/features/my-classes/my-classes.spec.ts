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

  /**
   * MP-01, asserted as an absence. "Moje zajęcia" is a READ now: the member sees what the club signed
   * them up for and has no control to undo it — the two tests that used to exercise the cancel, and
   * the refusal it could return, went with the route.
   */
  it('offers no way to cancel a booking', async () => {
    await respond([booking()]);

    const html = fixture.nativeElement as HTMLElement;

    expect(html.querySelectorAll('button').length).toBe(0);
    expect(html.textContent).not.toContain('Anuluj zapis');
  });

  it('says who does the booking, so an empty list is not a dead end', async () => {
    await respond([]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Zapisy prowadzi klub');
  });
});
