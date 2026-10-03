import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { MemberDetail, MembershipPassView } from '../../../core/admin/member-admin.models';
import { ToastService } from '../../../shared/toast/toast.service';
import { clubToday } from '../../../core/passes/club-today';
import { MemberPasses } from './member-passes';

const MEMBER: MemberDetail = {
  id: 'm1',
  userId: 'u1',
  displayName: 'Anna Kowalska',
  email: 'anna@test.local',
  membershipStatus: 'Active',
  accountStatus: 'Active',
  roles: ['User'],
  phoneNumber: null,
  street: null,
  houseNumber: null,
  postalCode: null,
  city: null,
  createdAt: '2026-09-01T08:00:00+00:00',
};

const PASS: MembershipPassView = {
  id: 'p1',
  typeName: 'Karnet 10 wejść',
  validFrom: '2026-09-01',
  validTo: '2026-09-30',
  entryCount: 10,
  entriesUsed: 3,
  entriesLeft: 7,
  issuedAt: '2026-09-01T08:00:00+00:00',
  coversToday: true,
  paidAt: null,
};

/** S-37: what issuing or editing a karnet booked from the member's group rosters — nothing here. */
const NO_ROSTER = { booked: 0, skipped: [] };

/**
 * This screen had no spec until S-19 changed its error handling — the slice's rule is that new
 * coverage arrives with a reason, not for its own sake.
 *
 * What is pinned here is the SPLIT the failure rule imposes on one screen: the issue form keeps its
 * banner (outlet 2) because the admin corrects the controls right under it, while revoking a row
 * reports through the toast (outlet 3) because a revoked row leaves nothing to correct. Plus the
 * distinction S-19 exists for at all — a 500 must not read as a karnet rule.
 */
describe('MemberPasses', () => {
  let fixture: ComponentFixture<MemberPasses>;
  let controller: HttpTestingController;
  let toasts: ToastService;

  async function create(passes: MembershipPassView[] = [PASS]) {
    TestBed.configureTestingModule({
      imports: [MemberPasses],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['id', 'm1']]) } },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    toasts = TestBed.inject(ToastService);
    fixture = TestBed.createComponent(MemberPasses);

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1'))).flush(MEMBER);
    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'))).flush(passes);
    await settle();
  }

  async function settle() {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function alertText(): string {
    return (fixture.nativeElement as HTMLElement).querySelector('.alert')?.textContent ?? '';
  }

  function toastText(): string {
    return toasts
      .toasts()
      .map((toast) => toast.message)
      .join(' ');
  }

  function toastTone(): string | undefined {
    return toasts.toasts().at(-1)?.tone;
  }

  function fillForm() {
    const component = fixture.componentInstance as unknown as {
      form: { setValue: (value: Record<string, unknown>) => void };
    };

    component.form.setValue({
      typeName: 'Karnet 10 wejść',
      validFrom: '2026-10-01',
      validTo: '2026-10-31',
      entryCount: 10,
      paid: false,
      paidAt: '',
    });
    fixture.detectChanges();
  }

  function submit() {
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
  }

  afterEach(() => {
    controller.verify();
  });

  it('renders the member and their karnet history', async () => {
    await create();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Anna Kowalska');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Karnet 10 wejść');
  });

  /** The issue form is outlet 2: the refusal names a rule the admin corrects in the controls below. */
  it('banners a refused karnet with the table sentence', async () => {
    await create();
    fillForm();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'))).flush(
      { reason: 'overlapping_pass' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(alertText()).toContain('nakładać');
    // Not a toast: the correction happens in this form, so the message belongs above it.
    expect(toasts.toasts()).toHaveLength(0);
  });

  /**
   * THE DISTINCTION S-19 EXISTS FOR. This used to read as "nie udało się zapisać karnetu", which
   * sends the admin to check dates that were never the problem. The API has no exception
   * middleware, so a body-less 500 is the realistic shape.
   */
  it('names a server fault rather than a karnet rule', async () => {
    await create();
    fillForm();
    submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(alertText()).toContain('po naszej stronie');
    expect(alertText()).not.toContain('karnetu');
  });

  /** A row action is outlet 3 — there is no control under a revoked row to correct. */
  it('toasts a refused revoke rather than bannering it', async () => {
    await create();

    (fixture.nativeElement as HTMLElement)
      .querySelectorAll<HTMLButtonElement>('button')
      .forEach((button) => {
        if (button.textContent?.includes('Usuń')) {
          button.click();
        }
      });
    fixture.detectChanges();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes/p1'))).flush(
      { reason: 'has_active_bookings' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    expect(toastText()).toContain('aktywne zapisy');
    expect(toastTone()).toBe('error');
    expect(alertText()).toBe('');
  });

  it('confirms a successful revoke and refetches the history', async () => {
    await create();

    (fixture.nativeElement as HTMLElement)
      .querySelectorAll<HTMLButtonElement>('button')
      .forEach((button) => {
        if (button.textContent?.includes('Usuń')) {
          button.click();
        }
      });
    fixture.detectChanges();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes/p1'))).flush(null);
    await settle();

    // Refetched rather than patched: entries used is derived server-side.
    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'))).flush([]);
    await settle();

    expect(toastText()).toContain('usunięty');
    expect(toastTone()).toBe('success');
  });

  // --- payment (pass-paid-flag) ------------------------------------------------------

  function click(text: string) {
    const button = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ).find((candidate) => candidate.textContent?.trim().startsWith(text));

    expect(button, `no button reading "${text}"`).toBeDefined();
    button!.click();
    fixture.detectChanges();
  }

  function paidBox(): HTMLInputElement {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      'app-checkbox input[type="checkbox"]',
    )!;
  }

  it('issues an unpaid karnet by default', async () => {
    await create();
    fillForm();
    submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'));
    expect(request.request.body.paidAt).toBeNull();
    request.flush({ ...PASS, roster: NO_ROSTER });
    await settle();
    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'))).flush([PASS]);
    await settle();
  });

  it('issues a paid karnet dated today when the box is ticked', async () => {
    await create();
    fillForm();
    paidBox().click();
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#paidAt')!.value,
    ).toBe(clubToday());

    submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'));
    expect(request.request.body.paidAt).toBe(clubToday());
    request.flush({ ...PASS, paidAt: clubToday(), roster: NO_ROSTER });
    await settle();
    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'))).flush([PASS]);
    await settle();
  });

  /** Editing never changes payment, so the edit neither shows the box nor sends `paidAt`. */
  it('sends no payment on an edit', async () => {
    await create();
    click('Edytuj');

    expect(paidBox()).toBeNull();

    submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes/p1'));
    expect(request.request.method).toBe('PUT');
    expect('paidAt' in request.request.body).toBe(false);
    request.flush({ ...PASS, roster: NO_ROSTER });
    await settle();
    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'))).flush([PASS]);
    await settle();
  });

  /** S-37: issuing books the holder into their groups' classes; what could not be booked is listed. */
  it('lists who the karnet could not book into the group classes', async () => {
    await create();
    fillForm();
    submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'));
    request.flush({
      ...PASS,
      roster: {
        booked: 2,
        skipped: [
          {
            memberId: 'm1',
            memberName: 'Anna Kowalska',
            classId: 'c1',
            startsAt: '2026-09-29T17:00:00Z',
            reason: 'class_full',
          },
        ],
      },
    });
    await settle();
    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1/passes'))).flush([PASS]);
    await settle();

    const panel = (fixture.nativeElement as HTMLElement).querySelector('.roster-report');
    expect(panel?.textContent).toContain('Brak wolnych miejsc');
    // The count rides the toast; the panel carries only the skips.
    expect(toastText()).toContain('Zapisano ze składu grupy: 2');
    expect(toastTone()).toBe('success');
  });

  it('marks a karnet paid through the overlay and replaces the row', async () => {
    await create();
    click('Oznacz jako opłacony');

    const overlay = (fixture.nativeElement as HTMLElement).querySelector(
      'app-pass-payment-overlay',
    );
    expect(overlay).not.toBeNull();
    expect(overlay!.querySelector<HTMLInputElement>('input[type="date"]')!.max).toBe(clubToday());

    overlay!.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    const request = await vi.waitFor(() => controller.expectOne('/api/passes/p1/paid'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ paidAt: clubToday() });
    request.flush({ ...PASS, paidAt: clubToday() });
    await settle();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('app-pass-payment-overlay'),
    ).toBeNull();
    expect(toastTone()).toBe('success');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Cofnij płatność');
  });

  /** A refusal in the overlay stays in the overlay, as its banner — the date is right there. */
  it('banners a refused payment inside the overlay', async () => {
    await create();
    click('Oznacz jako opłacony');

    const overlay = (fixture.nativeElement as HTMLElement).querySelector(
      'app-pass-payment-overlay',
    )!;
    overlay.querySelector('form')!.dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    (await vi.waitFor(() => controller.expectOne('/api/passes/p1/paid'))).flush(
      { reason: 'invalid_paid_at' },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(overlay.querySelector('.alert')?.textContent).toContain('Data płatności');
    expect(toasts.toasts()).toHaveLength(0);
  });

  it('clears a payment with an explicit null and a toast', async () => {
    await create([{ ...PASS, paidAt: '2026-09-02' }]);
    click('Cofnij płatność');

    const request = await vi.waitFor(() => controller.expectOne('/api/passes/p1/paid'));
    expect(request.request.body).toEqual({ paidAt: null });
    request.flush(PASS);
    await settle();

    expect(toastText()).toContain('Cofnięto');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Nieopłacony');
  });
});
