import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SwPush } from '@angular/service-worker';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';
import { CurrentUser } from './core/auth/auth.models';

const ADMIN: CurrentUser = {
  id: 'a1',
  email: 'admin@test.local',
  displayName: 'Admin',
  status: 'Active',
  roles: ['User', 'Admin'],
};

const MEMBER: CurrentUser = { ...ADMIN, id: 'm1', displayName: 'Member', roles: ['User'] };

describe('App', () => {
  function configure(auth: Partial<AuthService>) {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: auth as AuthService },
        // The shell renders the push opt-in prompt, which reaches SwPush through PushService. A
        // disabled worker is the honest stub: it is what a dev build and half the browsers in the
        // wild report, and it makes the prompt render nothing, so these tests stay about the header.
        { provide: SwPush, useValue: { isEnabled: false } as unknown as SwPush },
      ],
    });
  }

  function anonymous(): Partial<AuthService> {
    return {
      user: () => null,
      isAuthenticated: () => false,
      isAdmin: () => false,
      isTrainer: () => false,
      isActive: () => false,
    } as unknown as Partial<AuthService>;
  }

  it('should create the app', () => {
    configure(anonymous());
    expect(TestBed.createComponent(App).componentInstance).toBeTruthy();
  });

  it('renders the product name', async () => {
    configure(anonymous());

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Po Prostu Siłka');
  });

  it('shows no session controls to an anonymous visitor', async () => {
    configure(anonymous());

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('.shell-logout')).toBeNull();
  });

  it('offers logout once there is a session', async () => {
    configure({
      user: () => MEMBER,
      isAuthenticated: () => true,
      isAdmin: () => false,
      isTrainer: () => false,
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('.shell-logout')).not.toBeNull();
  });

  // Every OTHER member link in the header is gated on isActive(). This one is not, and that is the
  // point: the profile screen is where a member awaiting approval completes the contact details
  // S-13 added, so hiding it from them would hide the one screen they need. Its route and its API
  // group make the same choice.
  it('shows the profile link to a member who is not yet approved', async () => {
    configure({
      user: () => MEMBER,
      isAuthenticated: () => true,
      isAdmin: () => false,
      isTrainer: () => false,
      isActive: () => false,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/profile"]'),
    ).not.toBeNull();
  });

  it('shows no profile link to an anonymous visitor', async () => {
    configure(anonymous());

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/profile"]')).toBeNull();
  });

  // Hidden rather than disabled: a member who never sees the link never wonders why it refuses
  // them. The API enforces the same rule regardless.
  it('hides the approvals link from a non-admin member', async () => {
    configure({
      user: () => MEMBER,
      isAuthenticated: () => true,
      isAdmin: () => false,
      isTrainer: () => false,
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/admin/approvals"]'),
    ).toBeNull();
  });

  // The header's condition must match adminGuard and the backend Admin policy: an admin whose own
  // account is not approved is not an admin anywhere else either.
  it('hides the approvals link from an admin whose account is not active', async () => {
    configure({
      user: () => ADMIN,
      isAuthenticated: () => true,
      isAdmin: () => true,
      isTrainer: () => false,
      isActive: () => false,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/admin/approvals"]'),
    ).toBeNull();
  });

  it('shows the approvals link to an admin', async () => {
    configure({
      user: () => ADMIN,
      isAuthenticated: () => true,
      isAdmin: () => true,
      isTrainer: () => false,
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/admin/approvals"]'),
    ).not.toBeNull();
  });

  // Every approved account has a plan surface, whether or not one has been assigned yet - the
  // screen says so itself when there is none, which beats a missing link.
  it('shows the own-plan link to any active member', async () => {
    configure({
      user: () => MEMBER,
      isAuthenticated: () => true,
      isAdmin: () => false,
      isTrainer: () => false,
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/my-plan"]'),
    ).not.toBeNull();
    // ...but the authoring screen is not theirs.
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/trainer/plans"]'),
    ).toBeNull();
  });

  // The header's condition must match trainerGuard and the API's TrainerOrAdmin policy: Active AND
  // (Trainer OR Admin). An admin authors plans too - FR-015 was widened, not moved.
  it.each([
    ['trainer', { isTrainer: () => true, isAdmin: () => false }],
    ['admin', { isTrainer: () => false, isAdmin: () => true }],
  ])('shows the plans link to an active %s', async (_role, roles) => {
    configure({
      user: () => ADMIN,
      isAuthenticated: () => true,
      isActive: () => true,
      ...roles,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/trainer/plans"]'),
    ).not.toBeNull();
  });

  it('hides the plans link from a trainer whose account is not active', async () => {
    configure({
      user: () => MEMBER,
      isAuthenticated: () => true,
      isAdmin: () => false,
      isTrainer: () => true,
      isActive: () => false,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/trainer/plans"]'),
    ).toBeNull();
  });
});
