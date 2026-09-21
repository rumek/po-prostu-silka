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

  /**
   * The brand is a LOGO IMAGE, not text, so the product name lives in its alt attribute rather than
   * in textContent. That is what this asserts: the name must still be announced to a screen reader
   * and still shown if the image fails to load. Reading textContent here would pass on an <img> with
   * no alt at all, which is the regression worth catching.
   */
  it('renders the product name on the brand image', async () => {
    configure(anonymous());

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const brand = (fixture.nativeElement as HTMLElement).querySelector<HTMLImageElement>(
      'img.shell-brand',
    );

    expect(brand).not.toBeNull();
    expect(brand!.alt).toContain('Po Prostu Siłka');
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
  it('hides the member-list link from a non-admin member', async () => {
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
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/trainer/members"]'),
    ).toBeNull();
  });

  // The header's condition must match adminGuard and the backend Admin policy: an admin whose own
  // account is unusable is not an admin anywhere else either.
  it('hides the member-list link from an admin whose account is not active', async () => {
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
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/trainer/members"]'),
    ).toBeNull();
  });

  /**
   * S-22: "Plany" is retired, and an admin is NOT offered the trainer's member list — they reach
   * members, and so plans, through their own list. One role, one path to one list.
   */
  it('shows an admin neither Plany nor the trainer member list', async () => {
    configure({
      user: () => ADMIN,
      isAuthenticated: () => true,
      isAdmin: () => true,
      isTrainer: () => false,
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('a[href="/trainer/plans"]')).toBeNull();
    expect(root.querySelector('a[href="/trainer/members"]')).toBeNull();
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
    // ...but the authoring surface is not theirs.
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/trainer/members"]'),
    ).toBeNull();
  });

  /**
   * S-22 (UX-08): a trainer's way into a plan is their member list. The condition is STRICTER than
   * trainerGuard — trainer and NOT admin — so it can hide a link but never show one that bounces.
   */
  it('shows an active trainer Członkowie → /trainer/members, and no Plany', async () => {
    configure({
      user: () => MEMBER,
      isAuthenticated: () => true,
      isAdmin: () => false,
      isTrainer: () => true,
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    const link = root.querySelector<HTMLAnchorElement>('a[href="/trainer/members"]');
    expect(link?.textContent?.trim()).toBe('Członkowie');
    expect(root.querySelector('a[href="/trainer/plans"]')).toBeNull();
  });

  /**
   * The only account `!isAdmin()` changes anything for: an admin who also teaches. They keep their
   * admin member list and are not offered a second one.
   */
  it('shows an admin who also trains no trainer member list', async () => {
    configure({
      user: () => ADMIN,
      isAuthenticated: () => true,
      isAdmin: () => true,
      isTrainer: () => true,
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/trainer/members"]'),
    ).toBeNull();
  });

  it('hides the member-list link from a trainer whose account is not active', async () => {
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
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/trainer/members"]'),
    ).toBeNull();
  });

  /**
   * The bottom bar (S-12).
   *
   * The header tests above still assert the header, unchanged: the phone breakpoint hides .shell-nav
   * in CSS rather than dropping it from the DOM, so the links are still there to find. What is new is
   * the second navigation surface, and what matters about it here is that the SHELL decides whether
   * it exists at all — the bar itself is role-blind, and BottomNav's own spec covers its contents.
   */
  function bar(fixture: { nativeElement: unknown }): Element | null {
    return (fixture.nativeElement as HTMLElement).querySelector('.bottom-nav');
  }

  it('renders no bottom bar for an anonymous visitor', async () => {
    configure(anonymous());

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(bar(fixture)).toBeNull();
  });

  it('renders the bottom bar once there is a session', async () => {
    configure({
      user: () => MEMBER,
      isAuthenticated: () => true,
      isAdmin: () => false,
      isTrainer: () => false,
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(bar(fixture)).not.toBeNull();
  });

  /**
   * Gated on isAuthenticated() alone, deliberately — the same gate the header's nav uses. A Pending
   * member sees the bar, because /more is their only remaining path to /profile once the header's
   * links are hidden on a phone.
   */
  it('renders the bottom bar for a member who is not yet approved', async () => {
    configure({
      user: () => MEMBER,
      isAuthenticated: () => true,
      isAdmin: () => false,
      isTrainer: () => false,
      isActive: () => false,
    } as unknown as Partial<AuthService>);

    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    expect(bar(fixture)).not.toBeNull();
  });

  /** The design promise: the bar does not change shape between roles. */
  it('gives an admin the same five tabs as a member', async () => {
    const tabsFor = async (auth: Partial<AuthService>) => {
      TestBed.resetTestingModule();
      configure(auth);
      const fixture = TestBed.createComponent(App);
      await fixture.whenStable();
      // aria-label, not text: the tabs are icon-only, so this is the only name they carry.
      return [...(fixture.nativeElement as HTMLElement).querySelectorAll('.bottom-nav-tab')].map(
        (a) => a.getAttribute('aria-label'),
      );
    };

    const member = await tabsFor({
      user: () => MEMBER,
      isAuthenticated: () => true,
      isAdmin: () => false,
      isTrainer: () => false,
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    const admin = await tabsFor({
      user: () => ADMIN,
      isAuthenticated: () => true,
      isAdmin: () => true,
      isTrainer: () => false,
      isActive: () => true,
    } as unknown as Partial<AuthService>);

    expect(member.length).toBe(5);
    expect(admin).toEqual(member);
  });
});
