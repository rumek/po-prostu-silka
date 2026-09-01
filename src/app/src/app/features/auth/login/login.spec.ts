import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
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

  // Since S-01 a pending member logs in successfully — the routing decision is by status, not by a
  // login failure.
  it('routes a pending member to the awaiting-approval screen', async () => {
    fill('pending@test.local', 'TestPass_123');
    submit();

    (await expectLogin()).flush({ ...ACTIVE, status: 'Pending' });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/pending']);
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

  it('falls back to a generic message on an unexpected failure', async () => {
    fill('member@test.local', 'TestPass_123');
    submit();

    (await expectLogin()).flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('.alert');
    expect(alert?.textContent).toContain('Nie udało się zalogować');
  });
});
