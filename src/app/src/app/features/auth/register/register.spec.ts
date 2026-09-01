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

  function fill(
    displayName = 'Nowy Członek',
    email = 'nowy@test.local',
    password = 'TestPass_123',
  ) {
    const compiled = fixture.nativeElement as HTMLElement;

    for (const [id, value] of [
      ['displayName', displayName],
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

  function expectRegister() {
    return vi.waitFor(() => controller.expectOne('/api/auth/register'));
  }

  it('blocks submit on a password shorter than the API allows', async () => {
    fill(undefined, undefined, 'krotkie');
    submit();
    await fixture.whenStable();

    controller.expectNone('/api/auth/register');
  });

  it('trims the display name before sending it', async () => {
    fill('  Anna Kowalska  ');
    submit();

    const request = await expectRegister();
    expect(request.request.body.displayName).toBe('Anna Kowalska');

    request.flush(PENDING);
    await fixture.whenStable();
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

  // The API's Identity error codes are an open set, so the UI needs a branch that does not silently
  // report the wrong field.
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
