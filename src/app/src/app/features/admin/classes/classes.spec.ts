import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { DESK_MEDIA_QUERY } from '../../../core/layout/breakpoints';
import { ScheduledClass } from '../../../core/scheduling/class.models';
import { ScheduleCalendar } from '../../../shared/calendar/schedule-calendar';
import { ToastService } from '../../../shared/toast/toast.service';
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
  instructorMemberId: 'u1',
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
 * Stubs `matchMedia` so the DESK query answers `desk` and every other query answers false (the
 * calendar's week view stays off, as it does unstubbed). Returns a switch that flips the desk answer
 * the way a resized window would.
 */
function stubDesk(desk: boolean): (next: boolean) => void {
  const listeners: ((event: MediaQueryListEvent) => void)[] = [];

  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: (query: string) => ({
      matches: query === DESK_MEDIA_QUERY ? desk : false,
      addEventListener: (_: string, handler: (event: MediaQueryListEvent) => void) => {
        if (query === DESK_MEDIA_QUERY) {
          listeners.push(handler);
        }
      },
      removeEventListener: () => undefined,
    }),
  });

  return (next: boolean) => {
    desk = next;
    listeners.forEach((listener) => listener({ matches: next } as MediaQueryListEvent));
  };
}

/**
 * The panel renders the shared calendar (prd-v2 FR-017), so these assertions run against tiles
 * rather than list rows. Since S-20 a tile is a button that opens the class's actions overlay, so
 * every action is reached the way the admin reaches it: activate the tile, then press the action.
 * Everything the list did, it must still do.
 *
 * jsdom provides no `matchMedia`, so the calendar stays in its day view here — which is why the
 * fixtures are all on today — and the screen takes its desk fallback. The phone is stubbed
 * explicitly, in its own block at the end.
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

  /**
   * What this screen SAYS, as opposed to what it renders.
   *
   * Since S-19 every outcome of a row action goes to the toast rather than to a `.notice` banner
   * inside this component — the toast host is mounted once in the shell, so it is deliberately not
   * in this fixture. Reading the service reads the same thing the admin sees, and it also exposes
   * the TONE, which the single banner never carried.
   */
  function toastText(): string {
    return TestBed.inject(ToastService)
      .toasts()
      .map((toast) => toast.message)
      .join(' ');
  }

  function toastTone(): string | undefined {
    return TestBed.inject(ToastService).toasts().at(-1)?.tone;
  }

  function tiles(): HTMLElement[] {
    return Array.from(element().querySelectorAll('.calendar-tile'));
  }

  function tileFor(name: string): HTMLElement {
    return tiles().find((tile) => (tile.textContent ?? '').includes(name))!;
  }

  /** Activates a class's tile, as a click or Enter does, which opens its actions overlay (S-20). */
  function openActions(name: string): void {
    tileFor(name).click();
    fixture.detectChanges();
  }

  /** A button in the open actions overlay — an action, or a step's confirmation. */
  function overlayAction(label: string): HTMLButtonElement {
    return Array.from(
      element().querySelectorAll<HTMLButtonElement>(
        'app-class-actions-overlay .overlay-panel button',
      ),
    ).find((button) => (button.textContent ?? '').includes(label))!;
  }

  /** Opens a class's actions overlay and finds one of its actions; undefined when it is not offered. */
  function actionFor(name: string, label: string): HTMLButtonElement {
    openActions(name);

    return overlayAction(label);
  }

  function actionsOverlay(): HTMLElement | null {
    return element().querySelector('app-class-actions-overlay');
  }

  /** Moves the calendar a week back and answers with one class that already happened. */
  async function goToPastWeekWith(row: ScheduledClass): Promise<void> {
    element().querySelector<HTMLButtonElement>('[aria-label="Poprzedni tydzień"]')!.click();
    fixture.detectChanges();

    const past = await vi.waitFor(() => adminRequests()[0]);
    past.flush([{ ...row, startsAt: new Date(Date.now() - 7 * 86_400_000).toISOString() }]);
    await settle();
  }

  /** A past-week tile is a plain block: not a button, and activating it opens nothing. */
  function expectInert(name: string): void {
    expect(tileFor(name).tagName).not.toBe('BUTTON');

    tileFor(name).click();
    fixture.detectChanges();

    expect(actionsOverlay()).toBeNull();
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

    actionFor('Joga', 'Powiel').click();
    fixture.detectChanges();

    overlayAction('Powiel').click();
    await settle();

    controller.expectOne('/api/admin/classes/c1/duplicate').flush({
      created: 2,
      skippedWeeks: [3, 4],
    });
    await settle();

    // A batch where some weeks collided is a partial success, and saying "done" would leave the admin
    // believing in classes that were never created.
    expect(toastText()).toContain('Utworzono 2 kopie');
    expect(toastText()).toContain('Pominięto tydzień 3, 4');
    // `info`, not `success`: something the admin asked for did NOT happen, and a green tick over a
    // partial result would be the wrong answer.
    expect(toastTone()).toBe('info');

    adminRequests()[0].flush([JOGA]);
    await settle();
  });

  it('reports a clean duplicate without mentioning skipped weeks', async () => {
    await createWith([JOGA]);

    actionFor('Joga', 'Powiel').click();
    fixture.detectChanges();

    overlayAction('Powiel').click();
    await settle();

    controller.expectOne('/api/admin/classes/c1/duplicate').flush({ created: 4, skippedWeeks: [] });
    await settle();

    expect(toastText()).toContain('Utworzono 4 kopie');
    expect(toastText()).not.toContain('Pominięto');
    expect(toastTone()).toBe('success');

    adminRequests()[0].flush([JOGA]);
    await settle();
  });

  // --- delete ----------------------------------------------------------------

  it('asks for confirmation before deleting', async () => {
    await createWith([JOGA]);

    actionFor('Joga', 'Usuń').click();
    fixture.detectChanges();

    // Inline, never confirm() — that blocks the event loop and has no precedent here.
    expect(html()).toContain('Usunąć „Joga”');
    controller.expectNone('/api/admin/classes/c1');
  });

  it('removes the tile after a confirmed delete', async () => {
    await createWith([JOGA, PILATES]);

    actionFor('Joga', 'Usuń').click();
    fixture.detectChanges();

    overlayAction('Tak, usuń').click();
    await settle();

    controller.expectOne('/api/admin/classes/c1').flush(null);
    await settle();

    expect(tiles().length).toBe(1);
    expect(html()).toContain('Pilates');
    expect(html()).not.toContain('Joga');
  });

  it('keeps the tile and surfaces the error when a delete fails', async () => {
    await createWith([JOGA]);

    actionFor('Joga', 'Usuń').click();
    fixture.detectChanges();

    overlayAction('Tak, usuń').click();
    await settle();

    controller
      .expectOne('/api/admin/classes/c1')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await settle();

    expect(tiles().length).toBe(1);
    // A server fault now names itself instead of reading as a scheduling rule (S-19).
    expect(toastText()).toContain('po naszej stronie');
    expect(toastTone()).toBe('error');
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
    expect(body.instructorMemberId).toBe('u1');
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
    expect(toastText()).toContain('O tej porze są już inne zajęcia');
    expect(toastTone()).toBe('error');
    // The class that was not touched is untouched.
    expect(tileFor('Pilates').textContent).toContain('20:00');
  });

  it('withholds every action in a week that has already passed', async () => {
    await createWith([JOGA]);

    expect(actionFor('Joga', 'Usuń')).not.toBeUndefined();

    await goToPastWeekWith(JOGA);

    // Visible, but not editable — and the reason is on screen, or a tile that ignores a click reads
    // as broken.
    expect(tiles().length).toBe(1);
    expectInert('Joga');
    expect(html()).toContain('Ten tydzień już minął');
  });

  // --- FR-014: who signed up -------------------------------------------------

  const SIGNUP = {
    bookingId: 'b1',
    memberId: 'm1',
    displayName: 'Ala Kowalska',
    email: 'ala@example.test',
    bookedAt: todayAt(9),
  };

  /**
   * Opens the overlay for a class and answers BOTH requests it makes: the roster, and the member
   * list its sign-up picker offers (S-14). Nobody is offered here — this screen's tests are about
   * the calendar, and the picker has its own spec.
   */
  async function openBookings(name: string, rows: (typeof SIGNUP)[]): Promise<void> {
    actionFor(name, 'Zapisani').click();
    await settle();

    controller.expectOne('/api/admin/classes/c1/bookings').flush(rows);
    controller
      .expectOne('/api/admin/members?filter=Active&pageSize=100')
      .flush({ items: [], total: 0, page: 1, pageSize: 100 });
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
    // of the screen, so reading it meant scrolling away from the class it belongs to. It REPLACES the
    // actions overlay it was opened from rather than stacking on it.
    expect(actionsOverlay()).toBeNull();
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

    actionFor('Joga', 'Usuń').click();
    fixture.detectChanges();

    overlayAction('Tak, usuń').click();
    await settle();

    controller
      .expectOne('/api/admin/classes/c1')
      .flush({ reason: 'has_bookings' }, { status: 409, statusText: 'Conflict' });
    await settle();

    // "Cannot" without "why" reads as a broken button. The reason points at Zapisani, which is
    // where the admin can do something about it.
    expect(tiles().length).toBe(1);
    expect(toastText()).toContain('ktoś się już zapisał');
  });

  it('withholds Zapisani in a past week along with the rest of the actions', async () => {
    await createWith([JOGA]);

    expect(actionFor('Joga', 'Zapisani')).not.toBeUndefined();

    await goToPastWeekWith(JOGA);

    // Zapisani lives in the same overlay as the other three, so a tile that opens nothing withholds
    // it along with them — this pins that it stayed that way.
    expectInert('Joga');
  });
  // --- S-20: the actions overlay --------------------------------------------

  it('reaches every action on a 30-minute class', async () => {
    // Half an hour is 30px of tile: the actions projected into it were clipped away entirely. The
    // overlay is the same size whatever the class's length.
    await createWith([{ ...JOGA, durationMinutes: 30 }]);

    openActions('Joga');

    expect(actionsOverlay()).not.toBeNull();
    expect(
      element().querySelector('app-class-actions-overlay a[href="/admin/classes/c1"]'),
    ).not.toBeNull();
    expect(overlayAction('Powiel')).not.toBeUndefined();
    expect(overlayAction('Zapisani')).not.toBeUndefined();
    expect(overlayAction('Usuń')).not.toBeUndefined();
  });

  it('closes the overlay on Escape and on the backdrop', async () => {
    await createWith([JOGA]);

    openActions('Joga');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(actionsOverlay()).toBeNull();

    openActions('Joga');
    element()
      .querySelector<HTMLButtonElement>('app-class-actions-overlay .overlay-backdrop')!
      .click();
    fixture.detectChanges();
    expect(actionsOverlay()).toBeNull();
  });

  it('closes an open overlay when the calendar moves to another window', async () => {
    await createWith([JOGA]);

    openActions('Joga');
    expect(actionsOverlay()).not.toBeNull();

    element().querySelector<HTMLButtonElement>('[aria-label="Następny tydzień"]')!.click();
    fixture.detectChanges();

    // Its class may not even be on screen any more.
    expect(actionsOverlay()).toBeNull();

    adminRequests()[0].flush([]);
    await settle();
  });

  it('keeps the overlay open, marked, when an action fails', async () => {
    await createWith([JOGA]);

    actionFor('Joga', 'Usuń').click();
    fixture.detectChanges();
    overlayAction('Tak, usuń').click();
    await settle();

    controller
      .expectOne('/api/admin/classes/c1')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    await settle();

    expect(actionsOverlay()).not.toBeNull();
    expect(actionsOverlay()!.querySelector('.field-error')?.textContent).toContain('Nie udało się');
  });

  // --- S-09: cancelling, which is not deleting -------------------------------

  it('offers Odwołaj on a booked class and Usuń on an empty one', async () => {
    await createWith([JOGA, BOOKED]);

    // Exactly one of the two, per class — see canCancel in class-actions-overlay.
    expect(actionFor('Crossfit', 'Odwołaj')).not.toBeUndefined();
    expect(actionFor('Crossfit', 'Usuń')).toBeUndefined();

    expect(actionFor('Joga', 'Usuń')).not.toBeUndefined();
    expect(actionFor('Joga', 'Odwołaj')).toBeUndefined();
  });

  // A guard, not a case this screen reaches any more: the admin list no longer returns cancelled
  // classes. It stays pinned because the dead button it prevents only reappears if that changes.
  it('offers Usuń on a class already cancelled', async () => {
    await createWith([{ ...BOOKED, status: 'Cancelled' }]);

    // Cancelling it again is refused with already_cancelled, so offering it would be a dead end.
    expect(actionFor('Crossfit', 'Odwołaj')).toBeUndefined();
    expect(actionFor('Crossfit', 'Usuń')).not.toBeUndefined();
  });

  it('states how many people the cancellation will notify, and that it cannot be undone', async () => {
    await createWith([BOOKED]);

    actionFor('Crossfit', 'Odwołaj').click();
    fixture.detectChanges();

    expect(html()).toContain('Odwołać „Crossfit”');
    // 20 - 17. An admin about to email three people should be told it is three.
    expect(html()).toContain('Powiadomimy 3');
    expect(html()).toContain('nie można cofnąć');

    // Nothing has been sent yet - the confirmation is the question, not the answer.
    controller.expectNone('/api/admin/classes/c3/cancel');
  });

  it('takes the class off the calendar and says how many people were told', async () => {
    await createWith([BOOKED, PILATES]);

    actionFor('Crossfit', 'Odwołaj').click();
    fixture.detectChanges();

    overlayAction('Tak, odwołaj').click();
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
    expect(toastText()).toContain('Odwołano „Crossfit”');
    expect(toastText()).toContain('3 osoby');
    expect(toastTone()).toBe('success');
  });

  it('names class_started rather than saying only that it failed', async () => {
    await createWith([BOOKED]);

    actionFor('Crossfit', 'Odwołaj').click();
    fixture.detectChanges();

    overlayAction('Tak, odwołaj').click();
    await settle();

    controller
      .expectOne('/api/admin/classes/c3/cancel')
      .flush({ reason: 'class_started' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(tiles().length).toBe(1);
    expect(toastText()).toContain('już się rozpoczęły');
    expect(toastTone()).toBe('error');
  });

  /**
   * The dead end this slice closes. The tile sees ACTIVE bookings; the server refuses a delete once
   * a class has EVER been booked. A class everybody has since released therefore offers "Usuń" and
   * is refused — and without this route there is nothing left to click.
   */
  it('offers the cancel route out of a has_bookings refusal, and it works', async () => {
    await createWith([JOGA]);

    actionFor('Joga', 'Usuń').click();
    fixture.detectChanges();

    overlayAction('Tak, usuń').click();
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

    overlayAction('Tak, odwołaj').click();
    await settle();

    controller.expectOne('/api/admin/classes/c1/cancel').flush({ ...JOGA, status: 'Cancelled' });
    await settle();

    expect(toastText()).toContain('Odwołano „Joga”');
    expect(tiles().length).toBe(0);
  });

  it('withholds Odwołaj in a week that has already passed', async () => {
    await createWith([BOOKED]);

    expect(actionFor('Crossfit', 'Odwołaj')).not.toBeUndefined();

    await goToPastWeekWith(BOOKED);

    // In the same overlay as the other actions, so a past week withholds it too.
    expectInert('Crossfit');
  });

  // --- S-20: a desk tool, and a phone is told so ------------------------------

  describe('below the desk boundary', () => {
    let restoreMatchMedia: PropertyDescriptor | undefined;

    beforeEach(() => {
      restoreMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');
    });

    afterEach(() => {
      if (restoreMatchMedia) {
        Object.defineProperty(window, 'matchMedia', restoreMatchMedia);
      } else {
        delete (window as unknown as Record<string, unknown>)['matchMedia'];
      }
    });

    function createOnPhone(): void {
      stubDesk(false);

      TestBed.configureTestingModule({
        imports: [Classes],
        providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
      });

      controller = TestBed.inject(HttpTestingController);
      fixture = TestBed.createComponent(Classes);
      fixture.detectChanges();
    }

    it('renders a refusal and none of the calendar', async () => {
      createOnPhone();
      await settle();

      // An @if, not display:none — a phone never builds the library's DOM or its listeners.
      expect(element().querySelector('app-schedule-calendar')).toBeNull();
      expect(element().querySelector('.cal-week-view')).toBeNull();
      expect(html()).toContain('na komputerze');
      // The page header stays, so the admin still knows where they are.
      expect(element().querySelector('.page-header h1')?.textContent).toContain('Zajęcia');
    });

    it('points to the surfaces that do work on a phone', async () => {
      createOnPhone();
      await settle();

      const links = Array.from(
        element().querySelectorAll<HTMLAnchorElement>('.classes-desk-only a'),
      ).map((link) => link.getAttribute('href'));

      expect(links).toEqual(['/schedule', '/admin/classes/new', '/admin/class-types']);
    });

    it('fetches no classes', async () => {
      createOnPhone();
      await settle();

      // Nothing renders a calendar, so nothing emits the range that would trigger a load.
      // controller.verify() in afterEach would catch it too; this names the promise.
      expect(adminRequests()).toEqual([]);
    });

    it('drops an open overlay when the window narrows past the boundary', async () => {
      const flip = stubDesk(true);

      await createWith([JOGA]);

      actionFor('Joga', 'Powiel').click();
      fixture.detectChanges();
      expect(actionsOverlay()).not.toBeNull();

      flip(false);
      await settle();

      expect(element().querySelector('app-schedule-calendar')).toBeNull();
      // The state, not just the DOM: an overlay left open behind the refusal would reappear, pointing
      // at a class from a window nobody is looking at, the moment the admin widened the window again.
      expect(fixture.componentInstance['selected']()).toBeNull();

      flip(true);
      await settle();

      // Back at the desk the calendar is re-created and reloads the current week.
      adminRequests()[0].flush([JOGA]);
      await settle();

      expect(tiles().length).toBe(1);
      expect(actionsOverlay()).toBeNull();
    });
  });
});
