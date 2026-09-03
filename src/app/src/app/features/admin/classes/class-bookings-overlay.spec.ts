import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ClassBooking } from '../../../core/scheduling/booking.models';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { ClassBookingsOverlay } from './class-bookings-overlay';

const JOGA: ScheduledClass = {
  id: 'c1',
  classTypeId: 't1',
  name: 'Joga',
  description: null,
  startsAt: new Date(Date.now() + 86_400_000).toISOString(),
  durationMinutes: 60,
  instructorUserId: 'u1',
  instructor: 'Ola',
  capacity: 20,
  freeSpots: 18,
  status: 'Scheduled',
};

function signup(over: Partial<ClassBooking> = {}): ClassBooking {
  return {
    bookingId: over.bookingId ?? 'b1',
    memberUserId: over.memberUserId ?? 'm1',
    displayName: over.displayName ?? 'Ala Kowalska',
    email: over.email ?? 'ala@example.test',
    bookedAt: over.bookedAt ?? new Date().toISOString(),
  };
}

/** Hosts the overlay the way the admin screen does, so the inputs and outputs run as bound. */
@Component({
  imports: [ClassBookingsOverlay],
  template: `
    <app-class-bookings-overlay
      [row]="row()"
      (released)="releases = releases + 1"
      (closed)="closes = closes + 1"
    />
  `,
})
class Host {
  readonly row = signal<ScheduledClass>(JOGA);
  releases = 0;
  closes = 0;
}

/**
 * The admin's sign-up list (prd.md FR-014).
 *
 * It owns its own fetch and its own per-row state, like `class-create-overlay` — the screen keeps
 * only which class is open. The one thing that must reach the screen is `released`, because the
 * calendar tile behind this overlay draws a spot count that has to move with the list.
 */
describe('ClassBookingsOverlay', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  /**
   * Drains the microtask queue and renders.
   *
   * TWICE: a release awaits the HTTP promise and then updates two signals, so the handler runs one
   * turn after the response settles. One pass renders the state the overlay was in a moment ago.
   */
  async function settle(): Promise<void> {
    await fixture.whenStable();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function respond(rows: ClassBooking[]): Promise<void> {
    controller.expectOne('/api/admin/classes/c1/bookings').flush(rows);
    await settle();
  }

  function rows(): HTMLElement[] {
    return [...element().querySelectorAll<HTMLElement>('.bookings-row')];
  }

  function buttonWith(text: string): HTMLButtonElement | undefined {
    return [...element().querySelectorAll('button')].find((button) =>
      button.textContent?.includes(text),
    );
  }

  it('loads on init and lists each member with their email and booking time', async () => {
    await respond([signup(), signup({ bookingId: 'b2', displayName: 'Jan Nowak' })]);

    expect(rows().length).toBe(2);
    expect(element().textContent).toContain('Ala Kowalska');
    expect(element().textContent).toContain('ala@example.test');
    expect(element().textContent).toContain('Jan Nowak');
  });

  it('takes the occupied count from the class, not from the list length', async () => {
    // 20 capacity, 18 free — two taken. The list happens to hold one row here, and the header must
    // still say two, because the tile behind this overlay is drawing the same number.
    await respond([signup()]);

    expect(element().textContent).toContain('2 / 20');
  });

  it('says an empty class is empty rather than rendering nothing', async () => {
    await respond([]);

    expect(element().textContent).toContain('Nikt nie jest jeszcze zapisany');
  });

  it('offers a retry when the list fails to load', async () => {
    controller.expectOne('/api/admin/classes/c1/bookings').error(new ProgressEvent('failed'));
    await settle();

    expect(element().querySelector('[role="alert"]')).not.toBeNull();

    buttonWith('Spróbuj ponownie')!.click();
    await settle();

    await respond([signup()]);

    expect(rows().length).toBe(1);
  });

  it('releases a spot, removes the row and tells the screen', async () => {
    await respond([signup()]);

    buttonWith('Zwolnij miejsce')!.click();
    await settle();

    const request = controller.expectOne('/api/admin/classes/c1/bookings/b1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
    await settle();

    // Removed rather than refetched — the response already said the spot is gone.
    expect(rows().length).toBe(0);
    controller.expectNone('/api/admin/classes/c1/bookings');

    // The screen patches the tile's free-spot count off this.
    expect(host.releases).toBe(1);
  });

  it('keeps the row and names the refusal when a release fails', async () => {
    await respond([signup()]);

    buttonWith('Zwolnij miejsce')!.click();
    await settle();

    controller
      .expectOne('/api/admin/classes/c1/bookings/b1')
      .flush({ reason: 'conflict' }, { status: 409, statusText: 'Conflict' });
    await settle();

    // The row survives: removing it would tell the admin the spot was freed when it was not.
    expect(rows().length).toBe(1);
    expect(element().querySelector('.bookings-error')!.textContent).toContain(
      'Ktoś właśnie zmienił zapisy',
    );
    expect(host.releases).toBe(0);
  });

  it('closes on Escape, wherever focus happens to be', async () => {
    await respond([signup()]);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await settle();

    expect(host.closes).toBe(1);
  });

  it('closes when the backdrop is activated', async () => {
    await respond([signup()]);

    element().querySelector<HTMLButtonElement>('.overlay-backdrop')!.click();

    expect(host.closes).toBe(1);
  });
});
