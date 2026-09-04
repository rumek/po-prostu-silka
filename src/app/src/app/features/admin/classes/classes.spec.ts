import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { ScheduleCalendar } from '../../../shared/calendar/schedule-calendar';
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

/** Somebody is signed up. The only difference that decides which action the tile offers. */
const BOOKED: ScheduledClass = { ...JOGA, id: 'c3', name: 'Crossfit', freeSpots: 17 };

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

  // --- moving and resizing on the grid --------------------------------------

  /**
   * Ends a move or a resize on the calendar.
   *
   * The gesture itself is the shared component's business and is tested there; what this suite owns
   * is what the SCREEN does with the result.
   */
  async function rescheduleJoga(hour: number, durationMinutes: number) {
    const calendar = fixture.debugElement.query(By.directive(ScheduleCalendar))
      .componentInstance as ScheduleCalendar;
    const now = new Date();

    calendar.classRescheduled.emit({
      class: JOGA,
      startsAt: new Date(now.getFullYear(), now.getMonth(), now.getDate(), hour, 0),
      durationMinutes,
    });
    await settle();
  }

  function updateRequest(): TestRequest {
    return controller.expectOne(
      (request) => request.method === 'PUT' && request.url === '/api/admin/classes/c1',
    );
  }

  it('moves the class on screen at once, and sends back what the gesture cannot express', async () => {
    await createWith([JOGA]);

    await rescheduleJoga(21, 90);

    // OPTIMISTIC: the tile is already at the new time, before the server has answered. Snapping it
    // back for the length of a round trip is what would make the gesture feel broken.
    expect(tileFor('Joga').textContent).toContain('21:00');
    expect(tileFor('Joga').textContent).toContain('22:30');

    const body = updateRequest().request.body;

    // The update endpoint takes a whole ClassRequest — omitting these would blank them.
    expect(body.classTypeId).toBe('t1');
    expect(body.instructorUserId).toBe('u1');
    expect(body.capacity).toBe(20);
    expect(body.durationMinutes).toBe(90);
    expect(new Date(body.startsAt).getHours()).toBe(21);
  });

  it('puts the class back where it was when the server refuses the new time', async () => {
    await createWith([JOGA, PILATES]);

    await rescheduleJoga(21, 60);
    expect(tileFor('Joga').textContent).toContain('21:00');

    updateRequest().flush({ reason: 'time_conflict' }, { status: 409, statusText: 'Conflict' });
    await settle();

    // Back to 18:00 exactly, and said out loud — a block that silently returns reads as a bug.
    expect(tileFor('Joga').textContent).toContain('18:00');
    expect(html()).toContain('O tej porze są już inne zajęcia');
    // The class that was not touched is untouched.
    expect(tileFor('Pilates').textContent).toContain('20:00');
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

  // --- FR-014: who signed up -------------------------------------------------

  const SIGNUP = {
    bookingId: 'b1',
    memberUserId: 'm1',
    displayName: 'Ala Kowalska',
    email: 'ala@example.test',
    bookedAt: todayAt(9),
  };

  /** Opens the overlay for a class and answers the request it makes. */
  async function openBookings(name: string, rows: (typeof SIGNUP)[]): Promise<void> {
    actionIn(tileFor(name), 'Zapisani').click();
    await settle();

    controller.expectOne('/api/admin/classes/c1/bookings').flush(rows);
    await settle();
  }

  /** Buttons inside the sign-up overlay, which is a dialog rather than a panel below the grid. */
  function overlayButton(label: string): HTMLButtonElement {
    return Array.from(
      element().querySelectorAll<HTMLButtonElement>('app-class-bookings-overlay button'),
    ).find((button) => (button.textContent ?? '').includes(label))!;
  }

  it('opens the sign-up list as an overlay, not as a panel below the calendar', async () => {
    await createWith([{ ...JOGA, capacity: 20, freeSpots: 19 }]);
    await openBookings('Joga', [SIGNUP]);

    // A list of people is unbounded; a panel below the grid pushed a near-full class off the bottom
    // of the screen, so reading it meant scrolling away from the class it belongs to.
    const overlay = element().querySelector('app-class-bookings-overlay .overlay-panel')!;

    expect(overlay).not.toBeNull();
    expect(overlay.getAttribute('role')).toBe('dialog');
    expect(overlay.textContent).toContain('Zapisani na „Joga”');
    // The count comes off the class, not off the list: the overlay and the tile behind it must agree.
    expect(overlay.textContent).toContain('1 / 20');
    expect(overlay.textContent).toContain('Ala Kowalska');
    expect(overlay.textContent).toContain('ala@example.test');
  });

  it('says an empty class is empty rather than showing a blank overlay', async () => {
    await createWith([JOGA]);
    await openBookings('Joga', []);

    expect(html()).toContain('Nikt nie jest jeszcze zapisany');
  });

  it('releases a spot, removing the row and raising the tile count', async () => {
    await createWith([{ ...JOGA, capacity: 20, freeSpots: 19 }]);
    await openBookings('Joga', [SIGNUP]);

    expect(tileFor('Joga').textContent).toContain('19 / 20 wolnych');

    overlayButton('Zwolnij miejsce').click();
    await settle();

    const request = controller.expectOne('/api/admin/classes/c1/bookings/b1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
    await settle();

    expect(html()).toContain('Nikt nie jest jeszcze zapisany');
    // Both counts move: the tile behind the overlay, and the overlay's own header, which holds a
    // snapshot of the class it was opened with.
    expect(tileFor('Joga').textContent).toContain('20 / 20 wolnych');
    expect(element().querySelector('app-class-bookings-overlay')!.textContent).toContain('0 / 20');
  });

  it('closes the overlay without touching the tile', async () => {
    await createWith([{ ...JOGA, capacity: 20, freeSpots: 19 }]);
    await openBookings('Joga', [SIGNUP]);

    overlayButton('Zamknij').click();
    await settle();

    expect(element().querySelector('app-class-bookings-overlay')).toBeNull();
    expect(tileFor('Joga').textContent).toContain('19 / 20 wolnych');
  });

  it('names has_bookings when a delete is refused, rather than saying only that it failed', async () => {
    await createWith([JOGA]);

    actionIn(tileFor('Joga'), 'Usuń').click();
    fixture.detectChanges();

    panelButton('Tak, usuń').click();
    await settle();

    controller
      .expectOne('/api/admin/classes/c1')
      .flush({ reason: 'has_bookings' }, { status: 409, statusText: 'Conflict' });
    await settle();

    // "Cannot" without "why" reads as a broken button. The reason points at Zapisani, which is
    // where the admin can do something about it.
    expect(tiles().length).toBe(1);
    expect(html()).toContain('ktoś się już zapisał');
  });

  it('withholds Zapisani in a past week along with the rest of the actions', async () => {
    await createWith([JOGA]);

    expect(actionIn(tileFor('Joga'), 'Zapisani')).not.toBeUndefined();

    element().querySelector<HTMLButtonElement>('[aria-label="Poprzedni tydzień"]')!.click();
    fixture.detectChanges();

    const past = await vi.waitFor(() => adminRequests()[0]);
    past.flush([{ ...JOGA, startsAt: new Date(Date.now() - 7 * 86_400_000).toISOString() }]);
    await settle();

    // The new action is projected through the same template as the other three, so it is gated by
    // readOnly along with them — this pins that it stayed that way.
    expect(element().querySelector('.calendar-tile-actions')).toBeNull();
  });
  // --- S-09: cancelling, which is not deleting -------------------------------

  it('offers Odwołaj on a booked class and Usuń on an empty one', async () => {
    await createWith([JOGA, BOOKED]);

    // Exactly one of the two, per tile. A fifth button is what makes this row wrap on a phone.
    expect(actionIn(tileFor('Crossfit'), 'Odwołaj')).not.toBeUndefined();
    expect(actionIn(tileFor('Crossfit'), 'Usuń')).toBeUndefined();

    expect(actionIn(tileFor('Joga'), 'Usuń')).not.toBeUndefined();
    expect(actionIn(tileFor('Joga'), 'Odwołaj')).toBeUndefined();
  });

  // A guard, not a case this screen reaches any more: the admin list no longer returns cancelled
  // classes. It stays pinned because the dead button it prevents only reappears if that changes.
  it('offers Usuń on a class already cancelled', async () => {
    await createWith([{ ...BOOKED, status: 'Cancelled' }]);

    // Cancelling it again is refused with already_cancelled, so offering it would be a dead end.
    expect(actionIn(tileFor('Crossfit'), 'Odwołaj')).toBeUndefined();
    expect(actionIn(tileFor('Crossfit'), 'Usuń')).not.toBeUndefined();
  });

  it('states how many people the cancellation will notify, and that it cannot be undone', async () => {
    await createWith([BOOKED]);

    actionIn(tileFor('Crossfit'), 'Odwołaj').click();
    fixture.detectChanges();

    expect(html()).toContain('Odwołać „Crossfit”');
    // 20 - 17. An admin about to email three people should be told it is three.
    expect(html()).toContain('Powiadomimy 3');
    expect(html()).toContain('nie można cofnąć');

    // Nothing has been sent yet - the panel is the question, not the answer.
    controller.expectNone('/api/admin/classes/c3/cancel');
  });

  it('takes the class off the calendar and says how many people were told', async () => {
    await createWith([BOOKED, PILATES]);

    actionIn(tileFor('Crossfit'), 'Odwołaj').click();
    fixture.detectChanges();

    panelButton('Tak, odwołaj').click();
    await settle();

    controller.expectOne('/api/admin/classes/c3/cancel').flush({ ...BOOKED, status: 'Cancelled' });
    await settle();

    // The tile goes: the messages have gone out, the hour is free again for the overlap rule, and a
    // block still sitting there is a slot that looks taken and is not. The RECORD survives in the
    // database - that half of "not a delete" is the server's, not this screen's.
    // Asserted on TILES, not on the page text: the notice below names the class it just cancelled,
    // which is the point of the notice.
    expect(tiles().length).toBe(1);
    expect(tileFor('Crossfit')).toBeUndefined();
    expect(tileFor('Pilates')).not.toBeUndefined();

    // Saying so is the only place the admin learns the messages went out.
    expect(html()).toContain('Odwołano „Crossfit”');
    expect(html()).toContain('3 osoby');
  });

  it('names class_started rather than saying only that it failed', async () => {
    await createWith([BOOKED]);

    actionIn(tileFor('Crossfit'), 'Odwołaj').click();
    fixture.detectChanges();

    panelButton('Tak, odwołaj').click();
    await settle();

    controller
      .expectOne('/api/admin/classes/c3/cancel')
      .flush({ reason: 'class_started' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(tiles().length).toBe(1);
    expect(html()).toContain('już się rozpoczęły');
  });

  /**
   * The dead end this slice closes. The tile sees ACTIVE bookings; the server refuses a delete once
   * a class has EVER been booked. A class everybody has since released therefore offers "Usuń" and
   * is refused — and without this route there is nothing left to click.
   */
  it('offers the cancel route out of a has_bookings refusal, and it works', async () => {
    await createWith([JOGA]);

    actionIn(tileFor('Joga'), 'Usuń').click();
    fixture.detectChanges();

    panelButton('Tak, usuń').click();
    await settle();

    controller
      .expectOne('/api/admin/classes/c1')
      .flush({ reason: 'has_bookings' }, { status: 409, statusText: 'Conflict' });
    await settle();

    const escape = Array.from(element().querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      (b.textContent ?? '').includes('Odwołaj zamiast tego'),
    )!;
    expect(escape).not.toBeUndefined();

    escape.click();
    fixture.detectChanges();

    expect(html()).toContain('Odwołać „Joga”');

    panelButton('Tak, odwołaj').click();
    await settle();

    controller.expectOne('/api/admin/classes/c1/cancel').flush({ ...JOGA, status: 'Cancelled' });
    await settle();

    expect(html()).toContain('Odwołano „Joga”');
    expect(tiles().length).toBe(0);
  });

  it('withholds Odwołaj in a week that has already passed', async () => {
    await createWith([BOOKED]);

    expect(actionIn(tileFor('Crossfit'), 'Odwołaj')).not.toBeUndefined();

    element().querySelector<HTMLButtonElement>('[aria-label="Poprzedni tydzień"]')!.click();
    fixture.detectChanges();

    const past = await vi.waitFor(() => adminRequests()[0]);
    past.flush([{ ...BOOKED, startsAt: new Date(Date.now() - 7 * 86_400_000).toISOString() }]);
    await settle();

    // Projected through the same template as the other actions, so readOnly withholds it too.
    expect(element().querySelector('.calendar-tile-actions')).toBeNull();
  });
});
