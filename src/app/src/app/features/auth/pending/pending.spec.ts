import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { CurrentUser } from '../../../core/auth/auth.models';
import { Pending } from './pending';

const PENDING_USER: CurrentUser = {
  id: 'u1',
  email: 'pending@test.local',
  displayName: 'Oczekujący',
  status: 'Pending',
  roles: ['User'],
};

/** Typed by inference, so the spy keeps Router.navigate's signature. */
function spyOnNavigate() {
  return vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
}

describe('Pending', () => {
  let fixture: ComponentFixture<Pending>;
  let controller: HttpTestingController;
  let navigate: ReturnType<typeof spyOnNavigate>;

  async function create(isActive = false) {
    TestBed.configureTestingModule({
      imports: [Pending],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    // The real AuthService, with only the status signal stubbed — refresh() itself is under test.
    const auth = TestBed.inject(AuthService);
    vi.spyOn(auth, 'isActive').mockReturnValue(isActive);

    controller = TestBed.inject(HttpTestingController);
    navigate = spyOnNavigate();

    fixture = TestBed.createComponent(Pending);
    await fixture.whenStable();
  }

  afterEach(() => controller.verify());

  function press(): void {
    (fixture.nativeElement as HTMLElement).querySelector('button')!.click();
  }

  /**
   * The whole point of F1. /me reads the database and would report Active while the cookie's
   * account_status claim still said Pending, dropping the member into an app that answers 403 to
   * everything for up to 30 minutes. Only /api/auth/refresh re-mints the claim.
   */
  it('checks status via /api/auth/refresh and never /api/auth/me', async () => {
    await create();
    press();

    const request = await vi.waitFor(() => controller.expectOne('/api/auth/refresh'));
    expect(request.request.method).toBe('POST');

    controller.expectNone('/api/auth/me');

    request.flush(PENDING_USER);
    await fixture.whenStable();
  });

  it('stays put and says so when the member is still pending', async () => {
    await create();
    press();

    (await vi.waitFor(() => controller.expectOne('/api/auth/refresh'))).flush(PENDING_USER);
    await fixture.whenStable();
    fixture.detectChanges();

    // A button that appears to do nothing is worse than one reporting no change.
    expect((fixture.nativeElement as HTMLElement).querySelector('.notice')?.textContent).toContain(
      'wciąż czeka',
    );
    expect(navigate).not.toHaveBeenCalled();
  });

  it('navigates to the app once the refresh reports Active', async () => {
    await create();
    press();

    (await vi.waitFor(() => controller.expectOne('/api/auth/refresh'))).flush({
      ...PENDING_USER,
      status: 'Active',
    });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/']);
  });

  it('reports a failed check rather than failing silently', async () => {
    await create();
    press();

    (await vi.waitFor(() => controller.expectOne('/api/auth/refresh'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.alert')?.textContent).toContain(
      'Nie udało się sprawdzić',
    );
  });

  /**
   * The route carries authGuard only — activeMemberGuard redirects Pending members HERE, so listing
   * it would loop. That is why this redirect lives in the component (F5).
   */
  it('redirects an already-approved member away on load', async () => {
    await create(true);

    expect(navigate).toHaveBeenCalledWith(['/']);
  });
});
