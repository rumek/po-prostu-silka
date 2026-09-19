import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { CurrentUser, LoginFailureReason } from '../../../core/auth/auth.models';
import { Login } from './login';

const ACTIVE: CurrentUser = {
  id: 'u1',
  email: 'member@test.local',
  displayName: 'Member',
  status: 'Active',
  roles: ['User'],
};

/** Typed by inference, so the spy keeps Router.navigate's signature. */
function spyOnNavigate() {
  return vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
}

describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let controller: HttpTestingController;
  let navigate: ReturnType<typeof spyOnNavigate>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);
    navigate = spyOnNavigate();

    fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
  });

  afterEach(() => controller.verify());

  function fill(email: string, password: string): void {
    const compiled = fixture.nativeElement as HTMLElement;

    for (const [id, value] of [
      ['email', email],
      ['password', password],
    ] as const) {
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

  /**
   * vi.waitFor rather than a bare expectOne: the submit handler is async, so the request is issued a
   * microtask later. Draining that by hand is the failure that cost time in F-03.
   */
  function expectLogin() {
    return vi.waitFor(() => controller.expectOne('/api/auth/login'));
  }

  async function failWith(reason: LoginFailureReason): Promise<string> {
    fill('member@test.local', 'TestPass_123');
    submit();

    (await expectLogin()).flush({ reason }, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    return (fixture.nativeElement as HTMLElement).querySelector('.alert')?.textContent ?? '';
  }

  it('does not call the API while the form is invalid', async () => {
    fill('not-an-email', '');
    submit();
    await fixture.whenStable();

    controller.expectNone('/api/auth/login');

    // The messages have to become visible, or the button looks broken.
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.field-error').length).toBe(2);
  });

  it('routes an active member to the app', async () => {
    fill('member@test.local', 'TestPass_123');
    submit();

    (await expectLogin()).flush(ACTIVE);
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/']);
  });

  it('renders one non-specific message for invalid credentials', async () => {
    const message = await failWith('invalid_credentials');

    expect(message).toContain('Nieprawidłowy e-mail lub hasło');

    // The API refuses to distinguish a wrong password from an unknown address; the UI must not
    // imply that it can.
    expect(message).not.toContain('nie istnieje');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('renders the blocked message for a blocked account', async () => {
    expect(await failWith('blocked')).toContain('zablokowane');
  });

  /**
   * The register screen bounces a refused invitation here (S-17), and the redirect must not be
   * silent — otherwise the member arrives at a login form with no idea why and retries the same
   * dead link.
   */
  it('explains a refused invitation when the register screen sends one here', async () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: convertToParamMap({ reason: 'invalid-invitation' }) },
          },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    const withReason = TestBed.createComponent(Login);
    await withReason.whenStable();
    withReason.detectChanges();

    const alert = (withReason.nativeElement as HTMLElement).querySelector('.alert');
    expect(alert?.textContent).toContain('To zaproszenie już nie jest aktywne');
  });

  /** Absent parameter, nothing rendered — the ordinary visit must stay clean. */
  it('renders no invitation message on an ordinary visit', () => {
    expect((fixture.nativeElement as HTMLElement).querySelector('.alert')).toBeNull();
  });

  /**
   * A SERVER FAULT READS AS ONE (S-19). This used to assert the union's generic fallback — the same
   * sentence a business refusal this build does not recognise produces — which told the member to
   * check their credentials over a failure their credentials had nothing to do with. The API has no
   * exception middleware, so a body-less 500 is the realistic shape here.
   */
  it('names a server fault rather than blaming the credentials', async () => {
    fill('member@test.local', 'TestPass_123');
    submit();

    (await expectLogin()).flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('.alert');
    expect(alert?.textContent).toContain('po naszej stronie');
    expect(alert?.textContent).not.toContain('Nie udało się zalogować');
  });

  /** /login is the one endpoint behind a rate limiter (src/Api/Program.cs:150). */
  it('names rate limiting rather than blaming the credentials', async () => {
    fill('member@test.local', 'TestPass_123');
    submit();

    (await expectLogin()).flush(null, { status: 429, statusText: 'Too Many Requests' });
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('.alert');
    expect(alert?.textContent).toContain('Zbyt wiele prób');
  });
});
