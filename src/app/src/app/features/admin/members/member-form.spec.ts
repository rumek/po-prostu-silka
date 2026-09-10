import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { MemberDetail } from '../../../core/admin/member-admin.models';
import { MemberForm } from './member-form';

const LINKED: MemberDetail = {
  id: 'm1',
  userId: 'u1',
  displayName: 'Anna Kowalska',
  email: 'anna@test.local',
  membershipStatus: 'Active',
  accountStatus: 'Active',
  roles: ['User'],
  phoneNumber: '601202303',
  street: 'Polna',
  houseNumber: '7/2',
  postalCode: '00-002',
  city: 'Kraków',
  createdAt: '2026-09-01T08:00:00+00:00',
};

/** A record the club keeps for someone who never registered — the case S-14 exists for. */
const ACCOUNTLESS: MemberDetail = {
  ...LINKED,
  id: 'm2',
  userId: null,
  email: null,
  accountStatus: null,
  roles: [],
  phoneNumber: null,
  street: null,
  houseNumber: null,
  postalCode: null,
  city: null,
};

describe('MemberForm', () => {
  let fixture: ComponentFixture<MemberForm>;
  let controller: HttpTestingController;
  let navigated: unknown[][];

  /**
   * Creates the component on either route. `id` null is `/admin/members/new`, which loads nothing;
   * otherwise the edit route, whose initial GET is answered with `detail`.
   */
  async function createWith(id: string | null, detail?: MemberDetail) {
    navigated = [];

    // Reset first, so a test may create the component twice — the accountless/linked pair below
    // compares the two renderings, and TestBed refuses to be reconfigured once instantiated.
    TestBed.resetTestingModule();

    TestBed.configureTestingModule({
      imports: [MemberForm],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => id } } },
        },
        {
          provide: Router,
          useValue: {
            navigate: (commands: unknown[]) => {
              navigated.push(commands);
              return Promise.resolve(true);
            },
            // RouterLink in the template resolves its href through these.
            createUrlTree: () => ({}),
            serializeUrl: () => '',
          },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MemberForm);

    if (id !== null) {
      (await vi.waitFor(() => controller.expectOne(`/api/admin/members/${id}`))).flush(
        detail ?? null,
      );
    }

    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => controller.verify());

  function component(): MemberForm {
    return fixture.componentInstance;
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  function form(): any {
    // The form is protected; the spec reaches it the way the template does.
    return (component() as unknown as { form: unknown }).form;
  }

  it('creates a member with a name and nothing else', async () => {
    await createWith(null);

    form().patchValue({ displayName: 'Filip Bez Konta' });
    const submitted = (component() as unknown as { submit(): Promise<void> }).submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members'));

    expect(request.request.method).toBe('POST');
    expect(request.request.body.displayName).toBe('Filip Bez Konta');

    // EMPTY STRINGS CROSS AS NULLS. "Not given" is the record's state; "" would be a value the club
    // never entered, and the API would store it as one.
    expect(request.request.body.city).toBeNull();

    // NO ADDRESS AT ALL (S-17): the desk does not take one, and the member supplies it when they
    // register with their invitation. Asserted as absent from the payload, not as null — the field
    // is gone from the contract rather than left empty.
    expect(request.request.body).not.toHaveProperty('email');

    request.flush({ id: 'm9' }, { status: 201, statusText: 'Created' });
    await submitted;

    expect(navigated).toEqual([['/admin/members']]);
  });

  /**
   * The all-or-nothing rule, caught before the round trip. The server enforces it too and names
   * whichever field it reaches first; refusing here names the whole rule instead.
   */
  it('refuses a half-filled address without calling the API', async () => {
    await createWith(null);

    form().patchValue({ displayName: 'Pół Adresu', city: 'Warszawa' });
    await (component() as unknown as { submit(): Promise<void> }).submit();

    controller.expectNone('/api/admin/members');
    expect(navigated).toEqual([]);
  });

  it('sends a complete address when every field is filled', async () => {
    await createWith(null);

    form().patchValue({
      displayName: 'Anna Nowak',
      phoneNumber: '601202303',
      street: 'Polna',
      houseNumber: '7/2',
      postalCode: '00-002',
      city: 'Kraków',
    });

    const submitted = (component() as unknown as { submit(): Promise<void> }).submit();
    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members'));

    expect(request.request.body.postalCode).toBe('00-002');

    request.flush({ id: 'm9' }, { status: 201, statusText: 'Created' });
    await submitted;
  });

  it('prefills the form when editing and PUTs to the member', async () => {
    await createWith('m1', LINKED);

    expect(form().getRawValue().displayName).toBe('Anna Kowalska');
    expect(form().getRawValue().city).toBe('Kraków');

    form().patchValue({ displayName: 'Anna Nowa' });
    const submitted = (component() as unknown as { submit(): Promise<void> }).submit();

    const request = await vi.waitFor(() => controller.expectOne('/api/admin/members/m1'));

    expect(request.request.method).toBe('PUT');
    expect(request.request.body.displayName).toBe('Anna Nowa');

    request.flush(null, { status: 204, statusText: 'No Content' });
    await submitted;

    expect(navigated).toEqual([['/admin/members']]);
  });

  it('says the record has a login only when it has one', async () => {
    await createWith('m1', LINKED);
    expect(html()).toContain('ma konto w aplikacji');

    await createWith('m2', ACCOUNTLESS);
    expect(html()).not.toContain('ma konto w aplikacji');
  });

  /**
   * S-17: the form asks for no address, so there is no email box to find and nothing on the screen
   * that could produce one. What the record HOLDS is still shown in the list — reading an address
   * back and writing one are different questions.
   */
  it('offers no email field, and says where the address comes from instead', async () => {
    await createWith(null);

    expect((fixture.nativeElement as HTMLElement).querySelector('#email')).toBeNull();
    expect(html()).toContain('przy rejestracji z zaproszenia');
  });

  /**
   * A lost optimistic race still has to say so. This used to be the `email_taken` test; that code is
   * unreachable now, and `conflict` is the failure that still reaches this screen from a save.
   */
  it('maps a lost race onto its own message and keeps the admin on the form', async () => {
    await createWith('m2', ACCOUNTLESS);

    form().patchValue({ displayName: 'Zmieniony' });
    const submitted = (component() as unknown as { submit(): Promise<void> }).submit();

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m2'))).flush(
      { reason: 'conflict' },
      { status: 409, statusText: 'Conflict' },
    );

    await submitted;
    fixture.detectChanges();

    expect(html()).toContain('zmieniły się w międzyczasie');

    // The admin STAYS on the form with their input intact — navigating away on a failure would lose
    // everything they typed and tell them it saved.
    expect(navigated).toEqual([]);
  });

  it('reports a failed load instead of showing an empty form', async () => {
    navigated = [];

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [MemberForm],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'm1' } } } },
        {
          provide: Router,
          useValue: {
            navigate: () => Promise.resolve(true),
            createUrlTree: () => ({}),
            serializeUrl: () => '',
          },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MemberForm);

    (await vi.waitFor(() => controller.expectOne('/api/admin/members/m1'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });

    await fixture.whenStable();
    fixture.detectChanges();

    expect(html()).toContain('Nie udało się wczytać');
  });

  function html(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }
});
