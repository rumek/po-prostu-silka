import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { CurrentUser } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import {
  GroupRosterMember,
  GroupRosterView,
  RosterChange,
} from '../../core/scheduling/roster.models';
import { ToastService } from '../../shared/toast/toast.service';
import { GroupRoster } from './group-roster';

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

function member(over: Partial<GroupRosterMember> = {}): GroupRosterMember {
  return {
    memberId: over.memberId ?? 'm1',
    displayName: over.displayName ?? 'Anna Kowalska',
    hasAccount: over.hasAccount ?? true,
    addedAt: '2026-10-01T10:00:00Z',
    bookedUpcoming: over.bookedUpcoming ?? 2,
    gaps: over.gaps ?? [],
  };
}

function roster(
  members: GroupRosterMember[],
  over: Partial<GroupRosterView> = {},
): GroupRosterView {
  return {
    groupId: 'g1',
    name: 'Pon 18:00',
    isActive: true,
    capacity: over.capacity ?? 6,
    upcomingClassCount: 3,
    members,
  };
}

describe('GroupRoster', () => {
  let fixture: ComponentFixture<GroupRoster>;
  let controller: HttpTestingController;
  let toast: {
    success: ReturnType<typeof vi.fn>;
    info: ReturnType<typeof vi.fn>;
    error: ReturnType<typeof vi.fn>;
  };

  async function create(current: CurrentUser, view: GroupRosterView): Promise<void> {
    toast = { success: vi.fn(), info: vi.fn(), error: vi.fn() };

    TestBed.configureTestingModule({
      imports: [GroupRoster],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { user: () => current } as unknown as AuthService },
        { provide: ToastService, useValue: toast },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ id: 'g1' }),
              data: { parent: '/admin/class-groups' },
            },
          },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(GroupRoster);
    fixture.detectChanges();

    controller.expectOne('/api/groups/g1/roster').flush(view);
    await settle();
  }

  /** Twice, as the bookings overlay's spec does: the search resolves one microtask behind the flush. */
  async function settle(): Promise<void> {
    await fixture.whenStable();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function button(label: string): HTMLButtonElement {
    const found = [...element().querySelectorAll('button')].find((b) =>
      b.textContent?.includes(label),
    );
    if (!found) {
      throw new Error(`No button "${label}"`);
    }
    return found;
  }

  async function type(phrase: string): Promise<void> {
    const input = element().querySelector<HTMLInputElement>('#roster-add-search')!;
    input.value = phrase;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  afterEach(() => controller.verify());

  it('shows each member with their bookings and their gaps grouped by reason', async () => {
    await create(
      ADMIN,
      roster([
        member({
          bookedUpcoming: 1,
          gaps: [
            { classId: 'c2', startsAt: '2026-11-11T17:00:00Z', reason: 'no_valid_pass' },
            { classId: 'c3', startsAt: '2026-11-18T17:00:00Z', reason: 'no_valid_pass' },
          ],
        }),
        member({
          memberId: 'm2',
          displayName: 'Bartek Nowak',
          hasAccount: false,
          gaps: [{ classId: 'c3', startsAt: '2026-11-18T17:00:00Z', reason: 'bookable' }],
        }),
      ]),
    );

    const rows = element().querySelectorAll('li[appRow]');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('zapisany na 1 z 3 zajęć');
    expect(rows[0].querySelectorAll('.roster-gap')).toHaveLength(1);
    expect(rows[0].querySelector('.roster-gap')?.textContent).toContain('11.11');
    expect(rows[0].querySelector('.roster-gap')?.textContent).toContain('18.11');
    expect(rows[0].querySelector('.roster-gap')?.textContent).toContain('nie ma karnetu');
    expect(rows[1].textContent).toContain('bez konta');
    expect(rows[1].textContent).toContain('uzupełnij zapisy');
    expect(element().textContent).toContain('2/6 w składzie');
  });

  it('says so when the roster is empty', async () => {
    await create(ADMIN, roster([]));

    expect(element().querySelector('app-empty')?.textContent).toContain('Skład jest pusty');
  });

  it('searches the admin member list for an admin', async () => {
    await create(ADMIN, roster([]));

    await type('Ann');

    const request = await vi.waitFor(() =>
      controller.expectOne((r) => r.url === '/api/admin/members'),
    );
    expect(request.request.params.get('search')).toBe('Ann');
    request.flush({ items: [], total: 0, page: 1, pageSize: 20 });
    await settle();
  });

  it('searches the trainer member list for a trainer', async () => {
    await create(TRAINER, roster([]));

    await type('Ann');

    const request = await vi.waitFor(() =>
      controller.expectOne((r) => r.url === '/api/trainer/members'),
    );
    request.flush({ items: [], total: 0, page: 1, pageSize: 20 });
    await settle();
  });

  it('adds the chosen person and lists whoever could not be booked', async () => {
    await create(ADMIN, roster([]));

    await type('Ann');
    (await vi.waitFor(() => controller.expectOne((r) => r.url === '/api/admin/members'))).flush({
      items: [{ id: 'm1', displayName: 'Anna Kowalska', userId: 'u1' }],
      total: 1,
      page: 1,
      pageSize: 20,
    });
    await settle();

    const select = element().querySelector<HTMLSelectElement>('#roster-add-member')!;
    select.value = select.options[1].value;
    select.dispatchEvent(new Event('change'));
    await settle();

    button('Dopisz').click();
    const request = controller.expectOne('/api/groups/g1/roster');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ memberId: 'm1' });

    const change: RosterChange = {
      roster: roster([member({ bookedUpcoming: 2 })]),
      report: {
        booked: 2,
        skipped: [
          {
            memberId: 'm1',
            memberName: 'Anna Kowalska',
            classId: 'c3',
            startsAt: '2026-11-18T17:00:00Z',
            reason: 'no_entries_left',
          },
        ],
      },
    };
    request.flush(change);
    await settle();

    expect(element().querySelectorAll('li[appRow]')).toHaveLength(1);
    expect(element().querySelector('.roster-report')?.textContent).toContain('wolnych wejść');
    expect(toast.success).toHaveBeenCalledWith(expect.stringContaining('Dopisano na 2 zajęcia'));
  });

  it('refuses an add through the toast, in the roster table words', async () => {
    await create(ADMIN, roster([]));

    await type('Ann');
    (await vi.waitFor(() => controller.expectOne((r) => r.url === '/api/admin/members'))).flush({
      items: [{ id: 'm1', displayName: 'Anna Kowalska', userId: 'u1' }],
      total: 1,
      page: 1,
      pageSize: 20,
    });
    await settle();

    const select = element().querySelector<HTMLSelectElement>('#roster-add-member')!;
    select.value = select.options[1].value;
    select.dispatchEvent(new Event('change'));
    await settle();

    button('Dopisz').click();
    controller
      .expectOne('/api/groups/g1/roster')
      .flush({ reason: 'roster_full' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('Skład jest pełny'));
  });

  it('withholds the add block once the roster is at capacity', async () => {
    await create(ADMIN, roster([member(), member({ memberId: 'm2' })], { capacity: 2 }));

    expect(element().querySelector('#roster-add-search')).toBeNull();
    expect(element().querySelector('.roster-add')?.textContent).toContain('Skład jest pełny (2/2)');
  });

  it('removes a member only after the inline confirmation', async () => {
    await create(ADMIN, roster([member()]));

    button('Usuń ze składu').click();
    await settle();
    expect(element().textContent).toContain('Przyszłe zapisy w tej grupie zostaną zwolnione');

    button('Usuń').click();
    const request = controller.expectOne('/api/groups/g1/roster/m1');
    expect(request.request.method).toBe('DELETE');
    request.flush(roster([]));
    await settle();

    expect(element().querySelectorAll('li[appRow]')).toHaveLength(0);
    expect(toast.success).toHaveBeenCalled();
  });

  it('fills the gaps on "Uzupełnij zapisy"', async () => {
    await create(
      ADMIN,
      roster([
        member({ gaps: [{ classId: 'c3', startsAt: '2026-11-18T17:00:00Z', reason: 'bookable' }] }),
      ]),
    );

    button('Uzupełnij zapisy').click();
    const request = controller.expectOne('/api/groups/g1/roster/sync');
    expect(request.request.method).toBe('POST');
    request.flush({
      roster: roster([member({ bookedUpcoming: 3 })]),
      report: { booked: 1, skipped: [] },
    });
    await settle();

    expect(element().querySelector('.roster-gap')).toBeNull();
    expect(element().querySelector('.roster-report')).toBeNull();
    expect(toast.success).toHaveBeenCalledWith('Dopisano na 1 zajęcia.');
  });

  it('shows a refused load as the screen state', async () => {
    toast = { success: vi.fn(), info: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      imports: [GroupRoster],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { user: () => TRAINER } as unknown as AuthService },
        { provide: ToastService, useValue: toast },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'g1' }), data: {} } },
        },
      ],
    });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(GroupRoster);
    fixture.detectChanges();

    controller
      .expectOne('/api/groups/g1/roster')
      .flush(null, { status: 403, statusText: 'Forbidden' });
    await settle();

    expect(element().querySelector('.alert')?.textContent).toContain('Nie masz uprawnień');
    expect(toast.error).not.toHaveBeenCalled();
  });
});
