import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CurrentUser } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { Profile } from './profile';

const COMPLETE: CurrentUser = {
  id: 'u1',
  email: 'anna@test.local',
  displayName: 'Anna Kowalska',
  status: 'Active',
  roles: ['User'],
  phoneNumber: '123456789',
  street: 'Piłsudskiego',
  houseNumber: '12A/3',
  postalCode: '00-001',
  city: 'Warszawa',
};

/** What an account registered before S-13 looks like: a session, and no contact details at all. */
const INCOMPLETE: CurrentUser = {
  ...COMPLETE,
  phoneNumber: null,
  street: null,
  houseNumber: null,
  postalCode: null,
  city: null,
};

describe('Profile', () => {
  let fixture: ComponentFixture<Profile>;
  let controller: HttpTestingController;

  /**
   * The component reads the session signal in its constructor to pre-fill, so the signal has to hold
   * the user BEFORE createComponent — hence a helper rather than a shared beforeEach.
   */
  async function createWith(user: CurrentUser) {
    TestBed.configureTestingModule({
      imports: [Profile],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);

    // Through loadCurrentUser rather than by poking the private signal: this is the same path a
    // real cold load takes, so the test breaks if that path stops populating the session.
    const auth = TestBed.inject(AuthService);
    const loaded = auth.loadCurrentUser();
    controller.expectOne('/api/auth/me').flush(user);
    await loaded;

    fixture = TestBed.createComponent(Profile);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => controller.verify());

  function compiled(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function submit(): void {
    compiled().querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  function expectSave() {
    return vi.waitFor(() => controller.expectOne('/api/profile'));
  }

  it('pre-fills the form from session state without a request of its own', async () => {
    await createWith(COMPLETE);

    expect(compiled().querySelector<HTMLInputElement>('#phoneNumber')!.value).toBe('123456789');
    expect(compiled().querySelector<HTMLInputElement>('#street')!.value).toBe('Piłsudskiego');
    expect(compiled().querySelector<HTMLInputElement>('#postalCode')!.value).toBe('00-001');
    expect(compiled().querySelector<HTMLInputElement>('#city')!.value).toBe('Warszawa');
  });

  /**
   * The gym owns the name on the membership (FR-006 as rewritten). Asserted as "no input exists"
   * rather than "the input is disabled" — a disabled field still reads as one that might open up,
   * and the API has no field for either value at all.
   */
  it('renders name and email as text, with no editable control for either', async () => {
    await createWith(COMPLETE);

    expect(compiled().querySelector('#displayName')).toBeNull();
    expect(compiled().querySelector('#email')).toBeNull();

    const text = compiled().textContent ?? '';
    expect(text).toContain('Anna Kowalska');
    expect(text).toContain('anna@test.local');
  });

  it('prompts an account with no contact details to complete them', async () => {
    await createWith(INCOMPLETE);

    expect(compiled().querySelector('.notice')?.textContent).toContain('Uzupełnij swoje dane');
  });

  it('shows no prompt once the details are complete', async () => {
    await createWith(COMPLETE);

    expect(compiled().querySelector('.notice')).toBeNull();
  });

  it('sends only the five contact fields', async () => {
    await createWith(COMPLETE);
    submit();

    const request = await expectSave();
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      phoneNumber: '123456789',
      street: 'Piłsudskiego',
      houseNumber: '12A/3',
      postalCode: '00-001',
      city: 'Warszawa',
    });

    request.flush(COMPLETE);
    await fixture.whenStable();
  });

  /**
   * The response is the new session state, so a member who arrives incomplete and saves must see the
   * prompt disappear without a reload — that is the signal replacement doing its job.
   */
  it('confirms the save and clears the prompt from the response', async () => {
    await createWith(INCOMPLETE);

    for (const [id, value] of [
      ['phoneNumber', '123456789'],
      ['street', 'Piłsudskiego'],
      ['houseNumber', '12A/3'],
      ['postalCode', '00-001'],
      ['city', 'Warszawa'],
    ] as const) {
      const input = compiled().querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }

    submit();
    (await expectSave()).flush(COMPLETE);
    await fixture.whenStable();
    fixture.detectChanges();

    const notices = [...compiled().querySelectorAll('.notice')].map((n) => n.textContent ?? '');
    expect(notices.some((text) => text.includes('Dane zostały zapisane'))).toBe(true);
    expect(notices.some((text) => text.includes('Uzupełnij swoje dane'))).toBe(false);
  });

  it('surfaces invalid_postal_code on the postal-code control, not as a banner', async () => {
    await createWith(COMPLETE);
    submit();

    (await expectSave()).flush(
      { reason: 'invalid_postal_code' },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(compiled().querySelector('.alert')).toBeNull();
    expect(compiled().querySelector('#postalCode')?.getAttribute('aria-invalid')).toBe('true');
    expect(compiled().querySelector('#city')?.getAttribute('aria-invalid')).toBe('false');
  });

  it('falls back to a banner for an unrecognised failure', async () => {
    await createWith(COMPLETE);
    submit();

    (await expectSave()).flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(compiled().querySelector('.alert')?.textContent).toContain('Nie udało się zapisać');
  });

  it('blocks submit on a postal code the API would refuse', async () => {
    await createWith(COMPLETE);

    const input = compiled().querySelector<HTMLInputElement>('#postalCode')!;
    input.value = '00001';
    input.dispatchEvent(new Event('input'));

    submit();
    await fixture.whenStable();

    controller.expectNone('/api/profile');
  });
});
