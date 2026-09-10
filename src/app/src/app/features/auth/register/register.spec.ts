import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { CurrentUser } from '../../../core/auth/auth.models';
import { Register } from './register';

const REGISTERED: CurrentUser = {
  id: 'u1',
  email: 'nowy@test.local',
  displayName: 'Nowy Członek',
  status: 'Active',
  roles: ['User'],
};

const INVITATION_CODE = 'ABCD-2345';

/** Typed by inference, so the spy keeps Router.navigate's signature. */
function spyOnNavigate() {
  return vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
}

/**
 * The screen reads its code from the query string, so every test needs one — the guard has already
 * refused the case where there is none, and that is invitation.guard.spec.ts's subject.
 */
function stubRoute(queryParams: Record<string, string>) {
  return {
    provide: ActivatedRoute,
    useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
  };
}

describe('Register', () => {
  let fixture: ComponentFixture<Register>;
  let controller: HttpTestingController;
  let navigate: ReturnType<typeof spyOnNavigate>;

  async function createWith(queryParams: Record<string, string>) {
    // Reset first: a test that wants a different query string re-creates the module, and TestBed
    // refuses to be reconfigured once beforeEach has instantiated it.
    TestBed.resetTestingModule();

    TestBed.configureTestingModule({
      imports: [Register],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        stubRoute(queryParams),
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    navigate = spyOnNavigate();

    fixture = TestBed.createComponent(Register);
    await fixture.whenStable();
  }

  beforeEach(() => createWith({ invitationCode: INVITATION_CODE }));

  afterEach(() => controller.verify());

  /** Overrides are keyed by control id, so a test names only the field it cares about. */
  function fill(overrides: Partial<Record<string, string>> = {}) {
    const compiled = fixture.nativeElement as HTMLElement;

    const values: Record<string, string> = {
      email: 'nowy@test.local',
      password: 'TestPass_123',
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

  // --- the invitation code (S-17) -------------------------------------------

  /** IR-03: the code arrives in the link and the member never types it. */
  it('shows the code from the invitationCode query parameter', () => {
    const shown = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="invitation-code"]',
    );

    expect(shown?.textContent?.trim()).toBe(INVITATION_CODE);
  });

  /**
   * TEXT, NOT AN INPUT. A readonly box is still a box and invites the member to type into it; and
   * with no bound control there is no readonly-versus-disabled trap left to fall into either.
   */
  it('renders the code as text rather than as any form field', () => {
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('#memberCode')).toBeNull();
    expect(compiled.querySelector('input[readonly]')).toBeNull();

    // The form itself is down to the two things the member actually supplies.
    expect(compiled.querySelectorAll('form input')).toHaveLength(2);
  });

  /**
   * The API contract is THREE fields since S-17. A field silently added or dropped fails as a 400
   * the member cannot act on, so assert the whole shape rather than one key.
   */
  it('sends exactly the email, the password and the code', async () => {
    fill();
    submit();

    const request = await expectRegister();
    expect(request.request.body).toEqual({
      email: 'nowy@test.local',
      password: 'TestPass_123',

      // AS IT ARRIVED, dash and all — normalisation belongs to the API, which owns the alphabet.
      memberCode: INVITATION_CODE,
    });

    request.flush(REGISTERED);
    await fixture.whenStable();
  });

  /** A link a member pasted with a trailing space must still work. */
  it('trims whitespace the link brought along with the code', async () => {
    await createWith({ invitationCode: `  ${INVITATION_CODE}  ` });

    fill();
    submit();

    const request = await expectRegister();
    expect(request.request.body.memberCode).toBe(INVITATION_CODE);

    request.flush(REGISTERED);
    await fixture.whenStable();
  });

  // --- submission -----------------------------------------------------------

  it('blocks submit on a password shorter than the API allows', async () => {
    fill({ password: 'krotkie' });
    submit();
    await fixture.whenStable();

    controller.expectNone('/api/auth/register');
  });

  it('navigates to the dashboard on success', async () => {
    fill();
    submit();

    (await expectRegister()).flush(REGISTERED);
    await fixture.whenStable();

    // Straight to the dashboard (S-16, MP-03): the account works the moment it exists, and since
    // S-17 it arrives holding the record the club has been keeping.
    expect(navigate).toHaveBeenCalledWith(['/']);
  });

  // --- failures -------------------------------------------------------------

  /**
   * The payoff D8 bought with reactive forms: the server's answer lands on the control that caused
   * it, not in a banner the member has to map back onto a field themselves.
   *
   * <p>
   * AND IT MUST NOT REDIRECT. This failure is fixable right here, and sending the member away would
   * throw away the password they just typed. Only a refused CODE leaves the screen.
   * </p>
   */
  it('surfaces email_taken on the email control without navigating away', async () => {
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

    // The typed password survives, which is the whole reason this failure stays on the screen.
    expect(compiled.querySelector<HTMLInputElement>('#password')!.value).toBe('TestPass_123');
  });

  it('surfaces invalid_password on the password control without navigating away', async () => {
    fill();
    submit();

    (await expectRegister()).flush(
      { reason: 'invalid_password' },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.alert')).toBeNull();
    expect(compiled.querySelector('#password')?.getAttribute('aria-invalid')).toBe('true');
    expect(navigate).not.toHaveBeenCalled();
  });

  /**
   * A REFUSED CODE LEAVES THE SCREEN (S-17). The field is readonly, so there is nothing here for the
   * member to correct — an error under a box they cannot touch would be a dead end. The reason
   * travels in the query string, and the login screen renders it.
   */
  it.each(['unknown_member_code', 'invalid_member_code'] as const)(
    'sends a %s failure to /login with a reason the login screen renders',
    async (reason) => {
      fill();
      submit();

      (await expectRegister()).flush({ reason }, { status: 409, statusText: 'Conflict' });
      await fixture.whenStable();

      expect(navigate).toHaveBeenCalledWith(['/login'], {
        queryParams: { reason: 'invalid-invitation' },
      });
    },
  );

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
    expect(navigate).not.toHaveBeenCalled();
  });
});
