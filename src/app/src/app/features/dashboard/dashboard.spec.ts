import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { CurrentUser } from '../../core/auth/auth.models';
import { MembershipPassView } from '../../core/admin/member-admin.models';
import { MyBooking } from '../../core/scheduling/booking.models';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { Dashboard } from './dashboard';

const BOOKINGS_URL = '/api/bookings/mine';
const PLAN_URL = '/api/plans/mine';
const PASS_URL = '/api/passes/mine';
const FEED_URL = '/api/trainer/classes';

// The seeded admin's shape: Admin alone, no User.
const ADMIN: CurrentUser = {
  id: 'a1',
  email: 'admin@test.local',
  displayName: 'Admin',
  status: 'Active',
  membershipStatus: 'Active',
  roles: ['Admin'],
};

const MEMBER: CurrentUser = { ...ADMIN, id: 'm1', displayName: 'Ala', roles: ['User'] };
const TRAINER: CurrentUser = {
  ...ADMIN,
  id: 't1',
  displayName: 'Marek',
  roles: ['User', 'Trainer'],
};

const PASS: MembershipPassView = {
  id: 'k1',
  typeName: 'Karnet 8 wejść',
  validFrom: '2026-09-01',
  validTo: '2026-09-30',
  entryCount: 8,
  entriesUsed: 3,
  entriesLeft: 5,
  issuedAt: new Date('2026-09-01T10:00').toISOString(),
  coversToday: true,
};

function booking(over: Partial<MyBooking> = {}): MyBooking {
  return {
    bookingId: over.bookingId ?? 'b1',
    classId: over.classId ?? 'c1',
    name: over.name ?? 'Joga',
    description: null,
    startsAt: over.startsAt ?? new Date(Date.now() + 86_400_000).toISOString(),
    durationMinutes: over.durationMinutes ?? 60,
    instructor: over.instructor ?? 'Ola',
    bookedAt: new Date().toISOString(),
  };
}

function scheduled(over: Partial<ScheduledClass> = {}): ScheduledClass {
  return {
    id: over.id ?? 's1',
    classTypeId: 'ct1',
    name: over.name ?? 'Crossfit',
    description: null,
    startsAt: over.startsAt ?? new Date().toISOString(),
    durationMinutes: over.durationMinutes ?? 60,
    instructorMemberId: 'i1',
    instructor: over.instructor ?? 'Marek',
    capacity: 10,
    freeSpots: 5,
    status: 'Scheduled',
  };
}

/** A local wall-clock time on a given day offset, so bucketing does not depend on the machine's zone. */
function atLocalHour(dayOffset: number, hour: number): string {
  const date = new Date();
  date.setHours(0, 0, 0, 0);
  date.setDate(date.getDate() + dayOffset);
  date.setHours(hour);
  return date.toISOString();
}

/**
 * The landing screen (prd.md FR-023, FR-024).
 *
 * Two things these tests exist for. First, the PERSONA BRANCH (S-25): a member gets the two
 * member cards and never requests the staff feed; staff get "Twoje zajęcia" and never request
 * the `/mine` routes — each would be a guaranteed 403 you only see in the network tab. Second, the
 * LOCAL-MIDNIGHT WINDOW: a class that started earlier today belongs under "Dzisiaj", and the API's
 * default window would have dropped it.
 */
describe('Dashboard', () => {
  let fixture: ComponentFixture<Dashboard>;
  let controller: HttpTestingController;

  function configure(user: CurrentUser) {
    TestBed.configureTestingModule({
      imports: [Dashboard],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            user: () => user,
            isAuthenticated: () => true,
            isActive: () => user.status === 'Active',
            isAdmin: () => user.roles.includes('Admin'),
            isTrainer: () => user.roles.includes('Trainer'),
          } as unknown as AuthService,
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Dashboard);
    fixture.detectChanges();
  }

  /**
   * TWO ROUNDS: getMyPass() post-processes a 204 into null, so its promise settles a microtask after
   * the response.
   */
  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /**
   * Answers the karnet card's request (S-16). Every test has to, because the card loads on every
   * visit and `verify()` would otherwise find it outstanding. 204 by default — "no karnet" is the
   * state that changes nothing about what the other cards assert.
   */
  function flushPass(view: MembershipPassView | null = null): void {
    const request = controller.expectOne(PASS_URL);

    if (view === null) {
      request.flush(null, { status: 204, statusText: 'No Content' });
    } else {
      request.flush(view);
    }
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('greets the member and renders both member cards', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([booking({ name: 'Pilates', instructor: 'Ala T.' })]);
    flushPass(PASS);
    await settle();

    expect(element().querySelector('h1')!.textContent).toContain('Cześć, Ala!');
    expect(text()).toContain('Pilates');
    expect(text()).toContain('Karnet 8 wejść');
    controller.verify();
  });

  /** The plan has its own tab; the dashboard neither shows it nor asks for it. */
  it('carries no plan card and never requests the plan', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([booking()]);
    flushPass();
    await settle();

    expect(text()).not.toContain('Twój plan treningowy');
    expect(element().querySelectorAll('h1').length).toBe(1);
    controller.expectNone(PLAN_URL);
    controller.verify();
  });

  it('shows only the next booking, in the my-classes row, and links to the rest', async () => {
    configure(MEMBER);

    controller
      .expectOne(BOOKINGS_URL)
      .flush(
        [1, 2, 3, 4].map((n) =>
          booking({ bookingId: `b${n}`, classId: `c${n}`, name: `Zajęcia ${n}` }),
        ),
      );
    flushPass();
    await settle();

    const shown = element().querySelectorAll('app-booked-class');
    expect(shown.length).toBe(1);
    expect(shown[0].textContent).toContain('Zajęcia 1');
    expect(shown[0].querySelector('app-class-date')).not.toBeNull();
    expect(element().querySelector('a[href="/my-classes"]')).not.toBeNull();
    controller.verify();
  });

  // The link only when the card is hiding something.
  it('offers no "see all" when the one booking is all there is', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([booking()]);
    flushPass();
    await settle();

    expect(element().querySelectorAll('app-booked-class').length).toBe(1);
    expect(element().querySelector('a[href="/my-classes"]')).toBeNull();
    controller.verify();
  });

  it('never renders the staff section for a member, and fires no staff requests', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([booking()]);
    flushPass();
    await settle();

    expect(text()).not.toContain('Twoje zajęcia');
    controller.expectNone((r) => r.url === FEED_URL);
    controller.expectNone((r) => r.url === '/api/admin/classes');
    controller.verify();
  });

  /** A member's empty "Najbliższe zajęcia" no longer points at a schedule they cannot open. */
  it('sends a member with no bookings to the club, not to a schedule', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([]);
    flushPass();
    await settle();

    expect(text()).toContain('Zapisy prowadzi klub');
    expect(element().querySelector('a[href="/schedule"]')).toBeNull();
    controller.verify();
  });

  it.each([
    ['a trainer', TRAINER, '/schedule'],
    // jsdom has no matchMedia, so the desk fallback (true) sends the admin to the calendar.
    ['an admin', ADMIN, '/admin/classes'],
  ])('gives %s only "Twoje zajęcia" and requests no member data', async (_, user, grafik) => {
    configure(user);

    controller.expectOne((r) => r.url === FEED_URL).flush([]);
    await settle();

    expect(text()).toContain('Twoje zajęcia');
    expect(text()).toContain('Nie prowadzisz dziś zajęć.');
    expect(text()).toContain('Nie prowadzisz zajęć w najbliższym tygodniu.');
    expect(text()).not.toContain('Twój karnet');
    expect(text()).not.toContain('Twój plan treningowy');

    // Rendered with no rows: an admin who teaches nothing still reaches the club's calendar.
    const link = element().querySelector<HTMLAnchorElement>('a.dashboard-more')!;
    expect(link.textContent?.trim()).toBe('Zobacz grafik');
    expect(link.getAttribute('href')).toBe(grafik);

    for (const url of [BOOKINGS_URL, PLAN_URL, PASS_URL]) {
      controller.expectNone(url);
    }
    controller.expectNone((r) => r.url === '/api/admin/classes');
    controller.verify();
  });

  /**
   * THE BUG THIS CARD EXISTS TO AVOID. The endpoint's no-parameter window opens at `now`, so a class
   * that started an hour ago would vanish from the admin's "today". The request must ask from local
   * midnight, and the row must land in the today bucket rather than the upcoming one.
   */
  it('asks from local midnight and buckets a class that already started today as today', async () => {
    configure(TRAINER);

    const request = controller.expectOne((r) => r.url === FEED_URL);
    const from = new Date(request.request.params.get('from')!);
    const midnight = new Date();
    midnight.setHours(0, 0, 0, 0);

    expect(from.getTime()).toBe(midnight.getTime());

    request.flush([
      scheduled({ id: 'past-today', name: 'Poranny trening', startsAt: atLocalHour(0, 6) }),
      scheduled({ id: 'tomorrow', name: 'Jutrzejsza joga', startsAt: atLocalHour(1, 18) }),
    ]);
    await settle();

    const blocks = [...element().querySelectorAll('.dashboard-block')];
    const today = blocks.find((block) => block.textContent?.includes('Dzisiaj'))!;
    const upcoming = blocks.find((block) => block.textContent?.includes('Nadchodzące'))!;

    expect(today.textContent).toContain('Poranny trening');
    expect(today.textContent).not.toContain('Jutrzejsza joga');
    expect(upcoming.textContent).toContain('Jutrzejsza joga');
    // How full each class is: capacity 10 with 5 free is five taken.
    expect(today.querySelector('.dashboard-occupancy')!.textContent).toContain('5/10');
    controller.verify();
  });

  /**
   * The karnet card (S-16, MP-07). What it must show is entries LEFT — the number the member acts on
   * — and it must be read-only, which is the product decision MP-01 settles rather than a scope cut.
   */
  it('shows the karnet with entries left', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([]);
    flushPass(PASS);
    await settle();

    const card = [...element().querySelectorAll('.dashboard-card')].find((c) =>
      c.textContent?.includes('Karnet 8 wejść'),
    )!;

    expect(card).toBeDefined();
    expect(card.querySelector('.dashboard-count')!.textContent).toContain('5');
    expect(card.textContent).toContain('z 8');

    // The punch card: one mark per entry, the five left filled.
    expect(card.querySelectorAll('.dashboard-punches li').length).toBe(8);
    expect(card.querySelectorAll('.dashboard-punch-left').length).toBe(5);

    // READ-ONLY. Nothing on this card is a control — a member neither buys nor extends a karnet here.
    expect(card.querySelectorAll('button').length).toBe(0);
    controller.verify();
  });

  /**
   * 204 is "you hold no karnet", which is an ordinary state and not an error — and the empty state
   * names the CONSEQUENCE, because "brak karnetu" alone does not tell a member why nobody is signing
   * them up for anything.
   */
  it('shows a plain empty state when the member has no karnet', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([]);
    flushPass();
    await settle();

    const card = [...element().querySelectorAll('.dashboard-block')].find((c) =>
      c.textContent?.includes('Twój karnet'),
    )!;

    expect(card.textContent).toContain('Nie masz aktywnego karnetu');
    expect(card.querySelector('.empty')).not.toBeNull();
    // Not an error: an empty state styled as one would send the member to reception over nothing.
    expect(card.querySelector('[role="alert"]')).toBeNull();
    controller.verify();
  });

  /** One card failing must not blank the others — the reason there is no single loading flag. */
  it('keeps the other cards when one request fails', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).error(new ProgressEvent('failed'));
    flushPass(PASS);
    await settle();

    expect(element().querySelector('[role="alert"]')).not.toBeNull();
    expect(text()).toContain('Karnet 8 wejść');
    controller.verify();
  });

  it('retries a failed card without reloading the others', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).error(new ProgressEvent('failed'));
    flushPass();
    await settle();

    element().querySelector<HTMLButtonElement>('.link-button')!.click();
    fixture.detectChanges();

    controller.expectOne(BOOKINGS_URL).flush([booking({ name: 'Joga' })]);
    await settle();

    expect(text()).toContain('Joga');
    // The karnet card was not asked again.
    controller.expectNone(PASS_URL);
    controller.verify();
  });
});
