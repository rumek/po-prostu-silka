import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { ResetPassword } from './reset-password';
import { ToastService } from '../../../shared/toast/toast.service';

/** The token as it arrives from the link: already decoded by Angular's query-param parsing. */
const TOKEN = 'CfDJ8Abc+def/ghi==';

describe('ResetPassword', () => {
  let fixture: ComponentFixture<ResetPassword>;
  let controller: HttpTestingController;
  let navigate: ReturnType<typeof spyOnNavigate>;

  function spyOnNavigate() {
    return vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  }

  /**
   * The component reads the query string in its constructor, so the parameters have to be provided
   * before createComponent — hence a helper rather than a shared beforeEach.
   */
  async function createWith(params: Record<string, string>) {
    TestBed.configureTestingModule({
      imports: [ResetPassword],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(params) } },
        },
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    navigate = spyOnNavigate();

    fixture = TestBed.createComponent(ResetPassword);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const validLink = () => createWith({ email: 'anna@test.local', token: TOKEN });

  afterEach(() => controller.verify());

  function compiled(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function fill(newPassword: string, confirmation: string): void {
    for (const [id, value] of [
      ['newPassword', newPassword],
      ['confirmation', confirmation],
    ] as const) {
      const input = compiled().querySelector<HTMLInputElement>(`#${id}`)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
  }

  function submit(): void {
    compiled().querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  function expectReset() {
    return vi.waitFor(() => controller.expectOne('/api/auth/reset-password'));
  }

  it('sends both query parameters with the new password', async () => {
    await validLink();

    fill('NoweHaslo_456', 'NoweHaslo_456');
    submit();

    const request = await expectReset();

    expect(request.request.body).toEqual({
      email: 'anna@test.local',
      token: TOKEN,
      newPassword: 'NoweHaslo_456',
    });
    request.flush(null);
  });

  it('sends the member to the login screen on success, and says so there', async () => {
    await validLink();

    // After createWith: TestBed refuses an inject before the module is configured.
    const success = vi.spyOn(TestBed.inject(ToastService), 'success');

    fill('NoweHaslo_456', 'NoweHaslo_456');
    submit();
    (await expectReset()).flush(null, { status: 204, statusText: 'No Content' });

    await fixture.whenStable();

    // Not signed in here, so the confirmation is the only thing that proves the new password works.
    // It is raised BEFORE the navigation and survives it, because the toast host is mounted outside
    // <router-outlet> — which is what let the ?reset=ok query param go in S-23.
    expect(success).toHaveBeenCalledWith(
      'Hasło zostało zmienione. Zaloguj się przy użyciu nowego hasła.',
    );
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('blocks submit when the confirmation does not match', async () => {
    await validLink();

    fill('NoweHaslo_456', 'CosInnego_789');
    submit();

    controller.expectNone('/api/auth/reset-password');

    fixture.detectChanges();
    expect(compiled().querySelector('.field-error')!.textContent).toContain('nie są takie same');
  });

  it('offers a fresh link instead of a form when a parameter is missing', async () => {
    await createWith({ email: 'anna@test.local' });

    expect(compiled().querySelector('form')).toBeNull();
    expect(compiled().querySelector('.notice')!.textContent).toContain('nieprawidłowy');
  });

  /**
   * A spent, expired or wrong token all arrive as invalid_token, and none of them is fixable by
   * editing a field — so the form goes away and the only way forward is a new link.
   */
  it('treats a rejected token as a dead link', async () => {
    await validLink();

    fill('NoweHaslo_456', 'NoweHaslo_456');
    submit();
    (await expectReset()).flush(
      { reason: 'invalid_token' },
      { status: 400, statusText: 'Bad Request' },
    );

    await fixture.whenStable();
    fixture.detectChanges();

    expect(compiled().querySelector('form')).toBeNull();
    expect(compiled().querySelector('.notice')!.textContent).toContain('nieprawidłowy');
  });

  it('keeps the form and marks the field when the password is refused', async () => {
    await validLink();

    fill('NoweHaslo_456', 'NoweHaslo_456');
    submit();
    (await expectReset()).flush(
      { reason: 'invalid_new_password' },
      { status: 400, statusText: 'Bad Request' },
    );

    await fixture.whenStable();
    fixture.detectChanges();

    expect(compiled().querySelector('form')).not.toBeNull();
    expect(compiled().querySelector('.field-error')!.textContent).toContain('co najmniej');
  });
});
