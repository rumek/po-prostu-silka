import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { CurrentUser } from '../../../core/auth/auth.models';
import { Register } from './register';

const PENDING: CurrentUser = {
  id: 'u1',
  email: 'nowy@test.local',
  displayName: 'Nowy Członek',
  status: 'Pending',
  roles: ['User'],
};

/** Typed by inference, so the spy keeps Router.navigate's signature. */
function spyOnNavigate() {
  return vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
}

describe('Register', () => {
  let fixture: ComponentFixture<Register>;
  let controller: HttpTestingController;
  let navigate: ReturnType<typeof spyOnNavigate>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [Register],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    navigate = spyOnNavigate();

    fixture = TestBed.createComponent(Register);
    await fixture.whenStable();
  });

  afterEach(() => controller.verify());

  /** Overrides are keyed by control id, so a test names only the field it cares about. */
  function fill(overrides: Partial<Record<string, string>> = {}) {
    const compiled = fixture.nativeElement as HTMLElement;

    const values: Record<string, string> = {
      displayName: 'Nowy Członek',
      email: 'nowy@test.local',
      password: 'TestPass_123',
      phoneNumber: '123456789',
      street: 'Piłsudskiego',
      houseNumber: '12A/3',
      postalCode: '00-001',
      city: 'Warszawa',
      ...overrides,
    };

    for (const [id, value] of Object.entries(values)) {
      const input = compiled.querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
  }

  function submit(): void {
    (fixture.nativeElement as HTMLElement)
      .querySelector('form')!
      .dispatchEvent(new Event('submit'));
  }

  function expectRegister() {
    return vi.waitFor(() => controller.expectOne('/api/auth/register'));
  }

  it('blocks submit on a password shorter than the API allows', async () => {
    fill({ password: 'krotkie' });
    submit();
    await fixture.whenStable();

    controller.expectNone('/api/auth/register');
  });

  it('trims the display name before sending it', async () => {
    fill({ displayName: '  Anna Kowalska  ' });
    submit();

    const request = await expectRegister();
    expect(request.request.body.displayName).toBe('Anna Kowalska');

    request.flush(PENDING);
    await fixture.whenStable();
  });

  /**
   * The API contract is eight fields, not three (S-13). A field silently dropped from the payload
   * fails as a 400 the member cannot act on, so assert the whole shape rather than one key.
   */
  it('sends every contact field the API requires', async () => {
    fill();
    submit();

    const request = await expectRegister();
    expect(request.request.body).toEqual({
      displayName: 'Nowy Członek',
      email: 'nowy@test.local',
      password: 'TestPass_123',
      phoneNumber: '123456789',
      street: 'Piłsudskiego',
      houseNumber: '12A/3',
      postalCode: '00-001',
      city: 'Warszawa',

      // Null rather than absent: the field is optional to the MEMBER, not to the contract, and an
      // empty string would be a code the API has to refuse rather than "I have none" (S-14).
      memberCode: null,
    });

    request.flush(PENDING);
    await fixture.whenStable();
  });

  it('blocks submit on a postal code the API would refuse', async () => {
    fill({ postalCode: '00001' });
    submit();
    await fixture.whenStable();

    controller.expectNone('/api/auth/register');
  });

  it('navigates to the awaiting-approval screen on success', async () => {
    fill();
    submit();

    (await expectRegister()).flush(PENDING);
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/pending']);
  });

  /**
   * The payoff D8 bought with reactive forms: the server's answer lands on the control that caused
   * it, not in a banner the member has to map back onto a field themselves.
   */
  it('surfaces email_taken on the email control, not as a banner', async () => {
    fill();
    submit();

    (await expectRegister()).flush(
      { reason: 'email_taken' },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.alert')).toBeNull();
    expect(compiled.querySelector('#email')?.getAttribute('aria-invalid')).toBe('true');
    expect(compiled.querySelector('.field-error')?.textContent).toContain('To konto już istnieje');
    expect(navigate).not.toHaveBeenCalled();
  });

  /**
   * Same payoff as email_taken, for the contact codes: each maps to its own control, so a rejected
   * postal code must not surface as a banner the member has to map back onto a field themselves.
   */
  it('surfaces invalid_postal_code on the postal-code control, not as a banner', async () => {
    fill();
    submit();

    (await expectRegister()).flush(
      { reason: 'invalid_postal_code' },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.alert')).toBeNull();
    expect(compiled.querySelector('#postalCode')?.getAttribute('aria-invalid')).toBe('true');
    expect(compiled.querySelector('#email')?.getAttribute('aria-invalid')).toBe('false');
    expect(navigate).not.toHaveBeenCalled();
  });

  // The API's Identity error codes are an open set, so the UI needs a branch that does not silently
  // report the wrong field.
  // --- member code (S-14) ---------------------------------------------------

  /**
   * The field is optional and most people leave it empty; sending "" would make the API resolve a
   * code that was never typed.
   */
  it('sends the member code as null when the field is left empty', async () => {
    fill();
    submit();

    const request = await expectRegister();
    expect(request.request.body.memberCode).toBeNull();

    request.flush(PENDING);
    await fixture.whenStable();
  });

  /**
   * Sent AS TYPED, dash and all — normalisation (case, separators) belongs to the API, which owns
   * the alphabet. Only the surrounding whitespace a paste brings along is stripped.
   */
  it('sends the member code as typed, trimmed', async () => {
    fill({ memberCode: '  abcd-2345  ' });
    submit();

    const request = await expectRegister();
    expect(request.request.body.memberCode).toBe('abcd-2345');

    request.flush(PENDING);
    await fixture.whenStable();
  });

  /**
   * On the control, not as a banner: the one field the member can fix is the one the message has to
   * sit under. Same rule the email and postal-code failures already follow.
   */
  it('surfaces unknown_member_code on the code control', async () => {
    fill({ memberCode: 'ABCD-2345' });
    submit();

    (await expectRegister()).flush(
      { reason: 'unknown_member_code' },
      { status: 409, statusText: 'Conflict' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('#memberCode')?.getAttribute('aria-invalid')).toBe('true');
    expect(compiled.textContent).toContain('Ten kod już nie działa');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('surfaces invalid_member_code on the code control', async () => {
    fill({ memberCode: 'ZZ' });
    submit();

    (await expectRegister()).flush(
      { reason: 'invalid_member_code' },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('#memberCode')?.getAttribute('aria-invalid')).toBe('true');
    expect(compiled.textContent).toContain('osiem znaków');
  });

  it('falls back to a banner for an unrecognised failure', async () => {
    fill();
    submit();

    (await expectRegister()).flush(
      { reason: 'invalid_registration' },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.alert')?.textContent).toContain(
      'Nie udało się utworzyć konta',
    );
  });
});
