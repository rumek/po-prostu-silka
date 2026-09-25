import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '../../core/auth/auth.service';
import { CurrentUser } from '../../core/auth/auth.models';
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
    instructorMemberId: over.instructorMemberId ?? 'u1',
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
function staff(roles: string[]): CurrentUser {
  return {
    id: roles.join('-'),
    email: 'staff@test.local',
    displayName: 'Staff',
    status: 'Active',
    membershipStatus: 'Active',
    roles,
  };
}

const ADMIN = staff(['Admin']);
const TRAINER = staff(['User', 'Trainer']);

describe('Schedule', () => {
  let fixture: ComponentFixture<Schedule>;
  let controller: HttpTestingController;

  /** A STAFF screen since S-25: every case renders as a trainer or an admin. */
  function create(current: CurrentUser): void {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [Schedule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { user: () => current } as unknown as AuthService },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Schedule);
    fixture.detectChanges();
  }

  beforeEach(() => create(ADMIN));

  afterEach(() => controller.verify());

  function scheduleRequests(): TestRequest[] {
    return controller.match((request) => request.url === '/api/classes');
  }

  /**
   * Asserts the load asked for NO member data. Until S-25 a load fetched the caller's own bookings
   * beside the week; staff hold none, and `/api/bookings/mine` refuses them, so every load checks
   * here that the request did not come back.
   */
  function flushMine(): void {
    controller.expectNone('/api/bookings/mine');
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

  // --- S-25: a class opens its roster -----------------------------------------

  /** Opens the overlay for the only class on screen. */
  async function openFirstTile(): Promise<HTMLElement> {
    const html = fixture.nativeElement as HTMLElement;

    html.querySelector<HTMLButtonElement>('.calendar-tile-button')!.click();
    await settle();

    return html;
  }

  function tile(): ScheduledClass {
    const start = new Date();

    // The grid's last row (20:00–21:00): as late today as a class can still be drawn, so it stays in
    // the future for as much of the day as the grid allows.
    return at(new Date(start.getFullYear(), start.getMonth(), start.getDate(), 20, 0).toString(), {
      id: 'c1',
      freeSpots: 4,
      capacity: 12,
    });
  }

  async function showTile(): Promise<HTMLElement> {
    flushMine();
    scheduleRequests()[0].flush([tile()]);
    await settle();

    return openFirstTile();
  }

  function typeInPicker(html: HTMLElement, phrase: string): void {
    const box = html.querySelector<HTMLInputElement>('#add-member-search')!;
    box.value = phrase;
    box.dispatchEvent(new Event('input'));
  }

  it('opens the bookings overlay for a selected class', async () => {
    const html = await showTile();

    controller.expectOne('/api/admin/classes/c1/bookings').flush([]);
    await settle();

    expect(html.querySelector('app-class-bookings-overlay')).not.toBeNull();
    expect(html.textContent).toContain('Zapisani na „Joga”');
  });

  it("searches the admin member list from an admin's overlay", async () => {
    const html = await showTile();
    controller.expectOne('/api/admin/classes/c1/bookings').flush([]);
    await settle();

    typeInPicker(html, 'jan');
    (
      await vi.waitFor(() =>
        controller.expectOne('/api/admin/members?filter=Active&search=jan&pageSize=20'),
      )
    ).flush({ items: [], total: 0, page: 1, pageSize: 20 });
    await settle();
  });

  /** A trainer would get 403 on the admin list; their picker searches their own, name-only. */
  it("searches /api/trainer/members from a trainer's overlay, and says the grafik is theirs", async () => {
    create(TRAINER);
    const html = await showTile();
    controller.expectOne('/api/admin/classes/c1/bookings').flush([]);
    await settle();

    expect(html.textContent).toContain('tylko zajęcia, które prowadzisz');

    typeInPicker(html, 'jan');
    (
      await vi.waitFor(() => controller.expectOne('/api/trainer/members?search=jan&pageSize=20'))
    ).flush({ items: [], total: 0, page: 1, pageSize: 20 });
    await settle();

    controller.expectNone((request) => request.url === '/api/admin/members');
  });

  it('patches the tile when the overlay releases a spot', async () => {
    const html = await showTile();
    controller.expectOne('/api/admin/classes/c1/bookings').flush([
      {
        bookingId: 'b1',
        memberId: 'm1',
        userId: 'u1',
        displayName: 'Ala',
        email: 'ala@example.test',
        bookedAt: new Date().toISOString(),
      },
    ]);
    await settle();

    [...html.querySelectorAll<HTMLButtonElement>('button')]
      .find((b) => b.textContent?.includes('Zwolnij miejsce'))!
      .click();
    await settle();

    controller.expectOne('/api/admin/classes/c1/bookings/b1').flush(null);
    await settle();

    expect(html.textContent).toContain('7 / 12 miejsc zajętych');
  });
});
