import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { CurrentUser } from '../../core/auth/auth.models';
import { ExpiringPass, MembershipPassView } from '../../core/admin/member-admin.models';
import { MyBooking } from '../../core/scheduling/booking.models';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { Dashboard } from './dashboard';

const BOOKINGS_URL = '/api/bookings/mine';
const PLAN_URL = '/api/plans/mine';
const PASS_URL = '/api/passes/mine';
const FEED_URL = '/api/trainer/classes';
const EXPIRING_URL = '/api/admin/members/expiring-passes';

function ending(over: Partial<ExpiringPass> = {}): ExpiringPass {
  return { memberId: 'e1', displayName: 'Ewa Nowak', validTo: '2026-10-04', daysLeft: 2, ...over };
}

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
  paidAt: null,
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
    classGroupId: 'ct1',
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
    if (user === ADMIN) {
      controller.expectOne(EXPIRING_URL).flush({ items: [], total: 0 });
    }
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

    // pass-paid-flag: the member sees whether this karnet is paid.
    expect(card.textContent).toContain('Nieopłacony');
    controller.verify();
  });

  it('says a paid karnet is paid, without the day', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([]);
    flushPass({ ...PASS, paidAt: '2026-09-02' });
    await settle();

    const card = [...element().querySelectorAll('.dashboard-card')].find((c) =>
      c.textContent?.includes('Karnet 8 wejść'),
    )!;

    expect(card.textContent).toContain('Opłacony');
    expect(card.textContent).not.toContain('Nieopłacony');
    expect(card.textContent).not.toContain('Opłacony ·');
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
  // --- Admin: karnets ending (expiring-passes-dashboard) -------------------------------------------

  /** S-25: the card is the admin persona's. A trainer and a member never even ask for it. */
  it.each([
    ['a trainer', TRAINER],
    ['a member', MEMBER],
  ])('never requests the expiring karnets for %s', async (_, user) => {
    configure(user);

    if (user === TRAINER) {
      controller.expectOne((r) => r.url === FEED_URL).flush([]);
    } else {
      controller.expectOne(BOOKINGS_URL).flush([]);
      flushPass();
    }
    await settle();

    controller.expectNone(EXPIRING_URL);
    expect(text()).not.toContain('Kończą się karnety');
    controller.verify();
  });

  it("puts the admin's club card above the classes, in the server's order", async () => {
    configure(ADMIN);

    controller.expectOne((r) => r.url === FEED_URL).flush([]);
    controller.expectOne(EXPIRING_URL).flush({
      items: [
        ending({ memberId: 'a', displayName: 'Anna Dziś', daysLeft: 0, validTo: '2026-10-02' }),
        ending({ memberId: 'b', displayName: 'Bartek Jutro', daysLeft: 1, validTo: '2026-10-03' }),
        ending({ memberId: 'c', displayName: 'Celina Cztery', daysLeft: 4, validTo: '2026-10-06' }),
      ],
      total: 3,
    });
    await settle();

    const page = text();
    expect(page.indexOf('Kończą się karnety')).toBeLessThan(page.indexOf('Twoje zajęcia'));

    const rows = Array.from(element().querySelectorAll<HTMLElement>('.dashboard-expiring li'));
    expect(rows.map((row) => row.querySelector('.row-name')!.textContent!.trim())).toEqual([
      'Anna Dziś',
      'Bartek Jutro',
      'Celina Cztery',
    ]);
    expect(
      rows.map((row) => row.querySelector('.dashboard-expiring-days')!.textContent!.trim()),
    ).toEqual(['dziś', 'jutro', 'za 4 dni']);

    // Each row is the way to the renewal: that member's karnet screen.
    expect(rows[1].querySelector('a')!.getAttribute('href')).toBe('/admin/members/b/passes');

    // Nothing hidden, so no "see all".
    expect(page).not.toContain('Zobacz wszystkich');
    controller.verify();
  });

  it('offers "Zobacz wszystkich (N)" only when the card hides someone', async () => {
    configure(ADMIN);

    controller.expectOne((r) => r.url === FEED_URL).flush([]);
    controller.expectOne(EXPIRING_URL).flush({
      items: ['a', 'b', 'c', 'd', 'e'].map((id) => ending({ memberId: id })),
      total: 7,
    });
    await settle();

    const links = Array.from(element().querySelectorAll<HTMLAnchorElement>('a')).filter((a) =>
      (a.textContent ?? '').includes('Zobacz wszystkich (7)'),
    );
    // Two placements of one link — the header's on a phone, the card's bottom edge above it.
    expect(links.length).toBe(2);
    for (const link of links) {
      expect(link.getAttribute('href')).toBe('/admin/members?expiring=1');
    }
    controller.verify();
  });

  it('keeps the card with an empty state when nothing is ending', async () => {
    configure(ADMIN);

    controller.expectOne((r) => r.url === FEED_URL).flush([]);
    controller.expectOne(EXPIRING_URL).flush({ items: [], total: 0 });
    await settle();

    expect(text()).toContain('Kończą się karnety');
    expect(text()).toContain('Żaden karnet nie kończy się w ciągu 5 dni.');
    controller.verify();
  });

  it('shows a failed card with a retry that reloads only that card', async () => {
    configure(ADMIN);

    controller.expectOne((r) => r.url === FEED_URL).flush([]);
    controller.expectOne(EXPIRING_URL).error(new ProgressEvent('failed'));
    await settle();

    expect(text()).toContain('Nie udało się wczytać karnetów.');
    element().querySelector<HTMLButtonElement>('[role="alert"] .link-button')!.click();
    fixture.detectChanges();

    controller.expectOne(EXPIRING_URL).flush({ items: [ending()], total: 1 });
    await settle();

    expect(text()).toContain('Ewa Nowak');
    controller.expectNone((r) => r.url === FEED_URL);
    controller.verify();
  });
});
