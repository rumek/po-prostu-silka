import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { CurrentUser } from '../../core/auth/auth.models';
import { MembershipPassView } from '../../core/admin/member-admin.models';
import { MyBooking } from '../../core/scheduling/booking.models';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { TrainingPlanDetail } from '../../core/training/training-plan.models';
import { Dashboard } from './dashboard';

const BOOKINGS_URL = '/api/bookings/mine';
const PLAN_URL = '/api/plans/mine';
const PASS_URL = '/api/passes/mine';

const ADMIN: CurrentUser = {
  id: 'a1',
  email: 'admin@test.local',
  displayName: 'Admin',
  status: 'Active',
  roles: ['User', 'Admin'],
};

const MEMBER: CurrentUser = { ...ADMIN, id: 'm1', displayName: 'Ala', roles: ['User'] };

const PLAN: TrainingPlanDetail = {
  id: 'p1',
  name: 'Masa - jesień',
  memberId: 'm1',
  memberDisplayName: 'Ala',
  assignedByDisplayName: 'Marek Trener',
  createdAt: new Date('2026-09-01T10:00').toISOString(),
  items: [],
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
 * Two things these tests exist for. First, the ROLE BRANCH: the admin half must be invisible to a
 * member, and its two requests must not even be fired for one — a member's dashboard issuing two
 * guaranteed 403s is a bug you only see in the network tab. Second, the LOCAL-MIDNIGHT WINDOW: a
 * class that started earlier today belongs under "Dzisiaj", and the API's default window would have
 * dropped it.
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
   * TWO ROUNDS, following my-plan.spec: getMine() on the plan service post-processes a 204 into null,
   * so its promise settles a microtask after the response.
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
    controller.expectOne(PLAN_URL).flush(PLAN);
    flushPass();
    await settle();

    expect(text()).toContain('Cześć, Ala');
    expect(text()).toContain('Pilates');
    expect(text()).toContain('Masa - jesień');
    controller.verify();
  });

  it('shows at most three bookings and links to the full list when there are more', async () => {
    configure(MEMBER);

    controller
      .expectOne(BOOKINGS_URL)
      .flush([1, 2, 3, 4].map((n) => booking({ bookingId: `b${n}`, classId: `c${n}` })));
    controller.expectOne(PLAN_URL).flush(null, { status: 204, statusText: 'No Content' });
    flushPass();
    await settle();

    expect(element().querySelectorAll('.dashboard-list li').length).toBe(3);
    expect(element().querySelector('a[href="/my-classes"]')).not.toBeNull();
    controller.verify();
  });

  /**
   * Heading outline. The dashboard owns the only h1; a loaded plan card contributes exactly one h2
   * (the plan's name), not two — a static card title beside plan-summary's own heading would read to
   * a screen reader as two unrelated topics instead of a label and its content.
   */
  it('renders one heading per card, and only one h1 on the screen', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([booking()]);
    controller.expectOne(PLAN_URL).flush(PLAN);
    flushPass();
    await settle();

    expect(element().querySelectorAll('h1').length).toBe(1);

    const planCard = [...element().querySelectorAll('.dashboard-card')].find((card) =>
      card.textContent?.includes('Masa - jesień'),
    )!;

    expect(planCard.querySelectorAll('h2').length).toBe(1);
    expect(planCard.querySelector('h2')!.textContent).toContain('Masa - jesień');
    controller.verify();
  });

  /** ...but a card with no plan still needs a title, since there is no name to stand in for one. */
  it('keeps the static card title when there is no plan to name it', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([booking()]);
    controller.expectOne(PLAN_URL).flush(null, { status: 204, statusText: 'No Content' });
    flushPass();
    await settle();

    expect(text()).toContain('Twój plan treningowy');
    controller.verify();
  });

  /** The distinction /my-plan draws, preserved here: 204 is an empty state, not a failure. */
  it('renders "no plan" as a plain card, not an alert', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([booking()]);
    controller.expectOne(PLAN_URL).flush(null, { status: 204, statusText: 'No Content' });
    flushPass();
    await settle();

    expect(text()).toContain('Nie masz jeszcze przypisanego planu');
    expect(element().querySelector('.alert')).toBeNull();
    controller.verify();
  });

  it('never renders the admin section for a member, and fires no admin requests', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([booking()]);
    controller.expectOne(PLAN_URL).flush(PLAN);
    flushPass();
    await settle();

    expect(text()).not.toContain('Wymaga uwagi');
    // The point of the isAdmin() guard in ngOnInit: a member must not fire an endpoint that answers
    // 403. verify() is what proves it — an unexpected admin request would be an open request here.
    controller.verify();
  });

  it('renders the admin section on top of the member cards for an admin', async () => {
    configure(ADMIN);

    controller.expectOne(BOOKINGS_URL).flush([]);
    controller.expectOne(PLAN_URL).flush(null, { status: 204, statusText: 'No Content' });
    flushPass();
    controller.expectOne((r) => r.url === '/api/admin/classes').flush([]);
    await settle();

    // The approvals card that used to sit here is gone with the flow it counted (S-16, MP-03), so
    // "Wymaga uwagi" is now the day's classes alone.
    expect(text()).toContain('Wymaga uwagi');
    expect(text()).not.toContain('Zgłoszenia');
    controller.verify();
  });

  /**
   * THE BUG THIS CARD EXISTS TO AVOID. The endpoint's no-parameter window opens at `now`, so a class
   * that started an hour ago would vanish from the admin's "today". The request must ask from local
   * midnight, and the row must land in the today bucket rather than the upcoming one.
   */
  it('asks from local midnight and buckets a class that already started today as today', async () => {
    configure(ADMIN);

    controller.expectOne(BOOKINGS_URL).flush([]);
    controller.expectOne(PLAN_URL).flush(null, { status: 204, statusText: 'No Content' });
    flushPass();

    const request = controller.expectOne((r) => r.url === '/api/admin/classes');
    const from = new Date(request.request.params.get('from')!);
    const midnight = new Date();
    midnight.setHours(0, 0, 0, 0);

    expect(from.getTime()).toBe(midnight.getTime());

    request.flush([
      scheduled({ id: 'past-today', name: 'Poranny trening', startsAt: atLocalHour(0, 6) }),
      scheduled({ id: 'tomorrow', name: 'Jutrzejsza joga', startsAt: atLocalHour(1, 18) }),
    ]);
    await settle();

    const cards = [...element().querySelectorAll('.dashboard-card')];
    const today = cards.find((card) => card.textContent?.includes('Dzisiaj'))!;
    const upcoming = cards.find((card) => card.textContent?.includes('Nadchodzące'))!;

    expect(today.textContent).toContain('Poranny trening');
    expect(today.textContent).not.toContain('Jutrzejsza joga');
    expect(upcoming.textContent).toContain('Jutrzejsza joga');
    controller.verify();
  });

  /**
   * The karnet card (S-16, MP-07). What it must show is entries LEFT — the number the member acts on
   * — and it must be read-only, which is the product decision MP-01 settles rather than a scope cut.
   */
  it('shows the karnet with entries left', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).flush([]);
    controller.expectOne(PLAN_URL).flush(null, { status: 204, statusText: 'No Content' });
    flushPass(PASS);
    await settle();

    const card = [...element().querySelectorAll('.dashboard-card')].find((c) =>
      c.textContent?.includes('Karnet 8 wejść'),
    )!;

    expect(card).toBeDefined();
    expect(card.querySelector('.dashboard-count')!.textContent).toContain('5');
    expect(card.textContent).toContain('z 8');

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
    controller.expectOne(PLAN_URL).flush(null, { status: 204, statusText: 'No Content' });
    flushPass();
    await settle();

    const card = [...element().querySelectorAll('.dashboard-card')].find((c) =>
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
    controller.expectOne(PLAN_URL).flush(PLAN);
    flushPass();
    await settle();

    expect(element().querySelector('[role="alert"]')).not.toBeNull();
    expect(text()).toContain('Masa - jesień');
    controller.verify();
  });

  it('retries a failed card without reloading the others', async () => {
    configure(MEMBER);

    controller.expectOne(BOOKINGS_URL).error(new ProgressEvent('failed'));
    controller.expectOne(PLAN_URL).flush(PLAN);
    flushPass();
    await settle();

    element().querySelector<HTMLButtonElement>('.link-button')!.click();
    fixture.detectChanges();

    controller.expectOne(BOOKINGS_URL).flush([booking({ name: 'Joga' })]);
    await settle();

    expect(text()).toContain('Joga');
    // Neither the plan card nor the karnet card was asked again.
    controller.expectNone(PLAN_URL);
    controller.expectNone(PASS_URL);
    controller.verify();
  });
});
