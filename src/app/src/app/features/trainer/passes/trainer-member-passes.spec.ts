import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { MembershipPassView } from '../../../core/admin/member-admin.models';
import { clubToday } from '../../../core/passes/club-today';
import { ToastService } from '../../../shared/toast/toast.service';
import { TrainerMemberPasses } from './trainer-member-passes';

const CURRENT: MembershipPassView = {
  id: 'p2',
  typeName: 'Karnet 8 wejść',
  validFrom: '2026-10-01',
  validTo: '2026-10-30',
  entryCount: 8,
  entriesUsed: 2,
  entriesLeft: 6,
  issuedAt: '2026-10-01T08:00:00+00:00',
  coversToday: true,
  paidAt: '2026-10-01',
};

/** The point of the screen: an EXPIRED karnet that was never paid, which today's row cannot reach. */
const EXPIRED_UNPAID: MembershipPassView = {
  ...CURRENT,
  id: 'p1',
  validFrom: '2026-09-01',
  validTo: '2026-09-30',
  coversToday: false,
  paidAt: null,
};

const URL = '/api/trainer/members/m1/passes';

/**
 * The trainer's karnet screen (pass-paid-flag): read-only history plus marking paid/unpaid. Pinned:
 * it lists past karnets too, offers no issue/edit/revoke, settles an old debt through the shared
 * overlay, and treats a 404 as a screen state rather than a toast.
 */
describe('TrainerMemberPasses', () => {
  let fixture: ComponentFixture<TrainerMemberPasses>;
  let controller: HttpTestingController;
  let toasts: ToastService;

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function arrive() {
    TestBed.configureTestingModule({
      imports: [TrainerMemberPasses],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: new Map([['id', 'm1']]), data: { parent: '/trainer/members' } },
          },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    toasts = TestBed.inject(ToastService);
    fixture = TestBed.createComponent(TrainerMemberPasses);
  }

  async function create(passes: MembershipPassView[] = [CURRENT, EXPIRED_UNPAID]) {
    await arrive();
    (await vi.waitFor(() => controller.expectOne(URL))).flush({
      memberId: 'm1',
      displayName: 'Anna Kowalska',
      passes,
    });
    await settle();
  }

  function button(text: string, within: ParentNode = element()): HTMLButtonElement | undefined {
    return Array.from(within.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      b.textContent?.trim().startsWith(text),
    );
  }

  afterEach(() => controller.verify());

  it('lists every karnet, past ones included, with its payment state', async () => {
    await create();

    expect(element().querySelector('h1')?.textContent).toContain('Anna Kowalska');

    const rows = Array.from(element().querySelectorAll('li'));
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Opłacony');
    expect(rows[1].textContent).toContain('Nieopłacony');
  });

  /** Issuing, editing and revoking stay the admin's (S-16). */
  it('offers no issue, edit or revoke', async () => {
    await create();

    expect(element().querySelector('form')).toBeNull();
    expect(button('Edytuj')).toBeUndefined();
    expect(button('Usuń')).toBeUndefined();
    expect(button('Wystaw')).toBeUndefined();
  });

  it('settles an expired unpaid karnet through the overlay', async () => {
    await create();

    button('Oznacz jako opłacony')!.click();
    fixture.detectChanges();

    const overlay = element().querySelector('app-pass-payment-overlay')!;
    overlay.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const request = await vi.waitFor(() => controller.expectOne('/api/passes/p1/paid'));
    expect(request.request.body).toEqual({ paidAt: clubToday() });
    request.flush({ ...EXPIRED_UNPAID, paidAt: clubToday() });
    await settle();

    expect(element().querySelector('app-pass-payment-overlay')).toBeNull();
    expect(element().textContent).not.toContain('Nieopłacony');
    expect(toasts.toasts().at(-1)?.tone).toBe('success');
  });

  it('undoes a payment with an explicit null', async () => {
    await create([CURRENT]);

    button('Cofnij płatność')!.click();
    fixture.detectChanges();

    const request = await vi.waitFor(() => controller.expectOne('/api/passes/p2/paid'));
    expect(request.request.body).toEqual({ paidAt: null });
    request.flush({ ...CURRENT, paidAt: null });
    await settle();

    expect(element().textContent).toContain('Nieopłacony');
  });

  /** Unknown member or staff: nothing behind it to read, so a screen state, never a toast. */
  it('renders a 404 as the not-found state', async () => {
    await arrive();
    (await vi.waitFor(() => controller.expectOne(URL))).flush(null, {
      status: 404,
      statusText: 'Not Found',
    });
    await settle();

    expect(element().querySelector('.alert')).not.toBeNull();
    expect(toasts.toasts()).toHaveLength(0);
  });

  it('shows an empty state for a member with no karnet', async () => {
    await create([]);

    expect(element().querySelector('app-empty')?.textContent).toContain('żadnego karnetu');
  });
});
