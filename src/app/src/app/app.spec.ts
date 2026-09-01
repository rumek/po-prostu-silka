import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
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
      ],
    });
  }

  function anonymous(): Partial<AuthService> {
    return {
      user: () => null,
      isAuthenticated: () => false,
      isAdmin: () => false,
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
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('.shell-logout')).not.toBeNull();
  });

  // Hidden rather than disabled: a member who never sees the link never wonders why it refuses
  // them. The API enforces the same rule regardless.
  it('hides the approvals link from a non-admin member', async () => {
    configure({
      user: () => MEMBER,
      isAuthenticated: () => true,
      isAdmin: () => false,
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
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/admin/approvals"]'),
    ).not.toBeNull();
  });
});
