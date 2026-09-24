import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SwPush } from '@angular/service-worker';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';
import { CurrentUser } from './core/auth/auth.models';
import { ScreenLevel } from './core/layout/screen';
import { ScreenTitle } from './core/layout/screen-title';

function user(id: string, roles: string[], overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    id,
    email: `${id}@test.local`,
    displayName: id,
    status: 'Active',
    membershipStatus: 'Active',
    roles,
    ...overrides,
  };
}

const MEMBER = user('member', ['User']);
const TRAINER = user('trainer', ['User', 'Trainer']);
// The seeded admin's shape: Admin alone, no User.
const ADMIN = user('admin', ['Admin']);
const ADMIN_TRAINER = user('admin-trainer', ['Admin', 'Trainer']);
const BLOCKED_MEMBER = user('blocked', ['User'], { membershipStatus: 'Blocked' });

const ADMIN_LISTS = ['/admin/members', '/admin/class-types', '/admin/exercises'];

/**
 * The shell (S-25): the header and the bottom bar render the persona's links from
 * core/layout/navigation.ts. The table's own spec pins the exact lists; this pins that the shell
 * reads the persona, and the few per-persona facts the S-25 access matrix is built around.
 */
describe('App', () => {
  function configure(current: CurrentUser | null) {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            user: () => current,
            isAuthenticated: () => current !== null,
            isActive: () => current?.status === 'Active' && current?.membershipStatus === 'Active',
            isAdmin: () => current?.roles.includes('Admin') ?? false,
            isTrainer: () => current?.roles.includes('Trainer') ?? false,
          } as unknown as AuthService,
        },
        // The shell renders the push opt-in prompt, which reaches SwPush through PushService. A
        // disabled worker is the honest stub: it is what a dev build and half the browsers in the
        // wild report, and it makes the prompt render nothing, so these tests stay about the header.
        { provide: SwPush, useValue: { isEnabled: false } as unknown as SwPush },
      ],
    });
  }

  async function render(current: CurrentUser | null): Promise<HTMLElement> {
    configure(current);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function headerHrefs(root: HTMLElement): string[] {
    return [...root.querySelectorAll('.shell-nav a')].map((a) => a.getAttribute('href') ?? '');
  }

  function barLabels(root: HTMLElement): (string | null)[] {
    return [...root.querySelectorAll('.bottom-nav-tab')].map((a) => a.getAttribute('aria-label'));
  }

  it('should create the app', () => {
    configure(null);
    expect(TestBed.createComponent(App).componentInstance).toBeTruthy();
  });

  /**
   * The brand is a LOGO IMAGE, not text, so the product name lives in its alt attribute rather than
   * in textContent. Reading textContent here would pass on an <img> with no alt at all, which is the
   * regression worth catching.
   */
  it('renders the product name on the brand image', async () => {
    const root = await render(null);
    const brand = root.querySelector<HTMLImageElement>('img.shell-brand');

    expect(brand).not.toBeNull();
    expect(brand!.alt).toContain('Po Prostu Siłka');
  });

  it('shows no session controls, no profile link and no bar to an anonymous visitor', async () => {
    const root = await render(null);

    expect(root.querySelector('.shell-logout')).toBeNull();
    expect(root.querySelector('a[href="/profile"]')).toBeNull();
    expect(root.querySelector('.bottom-nav')).toBeNull();
  });

  it('gives a member their classes and plan, and no schedule', async () => {
    const hrefs = headerHrefs(await render(MEMBER));

    expect(hrefs).toEqual(['/', '/my-classes', '/my-plan', '/profile']);
    expect(hrefs).not.toContain('/schedule');
  });

  it('gives a trainer the schedule and their member list, and no plan of their own', async () => {
    const hrefs = headerHrefs(await render(TRAINER));

    expect(hrefs).toEqual(['/', '/schedule', '/trainer/members', '/profile']);
    expect(hrefs).not.toContain('/my-plan');
    expect(hrefs).not.toContain('/my-classes');
  });

  /** The gap S-25 was opened for: before it, a desktop admin reached these lists from no menu. */
  it('puts every admin list in the header for an admin', async () => {
    const hrefs = headerHrefs(await render(ADMIN));

    for (const href of ADMIN_LISTS) {
      expect(hrefs).toContain(href);
    }
    // jsdom has no matchMedia, so the desk fallback (true) applies: Grafik is the calendar.
    expect(hrefs).toContain('/admin/classes');
    expect(hrefs).not.toContain('/my-plan');
  });

  /** Admin wins over Trainer: the admin set, and no second member list. */
  it('gives an admin who also trains the admin set and no trainer member list', async () => {
    const hrefs = headerHrefs(await render(ADMIN_TRAINER));

    for (const href of ADMIN_LISTS) {
      expect(hrefs).toContain(href);
    }
    expect(hrefs).not.toContain('/trainer/members');
  });

  /**
   * A blocked account has no persona. Moje konto stays: the profile screen and its API group are
   * gated on a session alone, so the one screen the account can still use stays reachable.
   */
  it('gives a blocked account only Moje konto and logout', async () => {
    const root = await render(BLOCKED_MEMBER);

    expect(headerHrefs(root)).toEqual(['/profile']);
    expect(root.querySelector('.shell-logout')).not.toBeNull();
  });

  /**
   * The bar (S-12) exists only with a session — the same gate the header uses, because the shell
   * renders before any guard runs. Since S-25 its tabs are the persona's.
   */
  it('renders the persona tabs in the bottom bar', async () => {
    expect(barLabels(await render(MEMBER))).toEqual(['Start', 'Zajęcia', 'Plan', 'Więcej']);
  });

  /**
   * The phone's app bar (mobile-native-feel), per screen level. CSS decides the WIDTH it shows at,
   * and jsdom applies none, so these pin what is rendered: the logo stays in the DOM throughout
   * (the desktop header shows it), and the bar's h1 and back button appear only where they belong.
   */
  describe('app bar', () => {
    async function renderAt(
      level: ScreenLevel,
      title: string,
      parent: string | null = null,
      current: CurrentUser | null = MEMBER,
    ): Promise<HTMLElement> {
      configure(current);
      TestBed.inject(ScreenTitle).resolve(title, level, parent);
      const fixture = TestBed.createComponent(App);
      await fixture.whenStable();
      return fixture.nativeElement as HTMLElement;
    }

    it('shows the logo and no title on a brand screen', async () => {
      const root = await renderAt('brand', 'Start');

      expect(root.querySelector('img.shell-brand')).not.toBeNull();
      expect(root.querySelector('.shell-title')).toBeNull();
      expect(root.querySelector('.shell-back')).toBeNull();
    });

    /** Start's logo bar scrolls away with the greeting; every titled bar stays pinned. */
    it('unpins the bar on Start alone', async () => {
      const start = await renderAt('brand', 'Start');
      expect(start.querySelector('header.shell-header')!.classList).toContain('is-home');

      TestBed.resetTestingModule();
      const tab = await renderAt('tab', 'Zajęcia');
      expect(tab.querySelector('header.shell-header')!.classList).not.toContain('is-home');
    });

    it('titles a tab screen, with no way back', async () => {
      const root = await renderAt('tab', 'Zajęcia');

      expect(root.querySelector('h1.shell-title')!.textContent!.trim()).toBe('Zajęcia');
      expect(root.querySelector('.shell-back')).toBeNull();
    });

    it('gives a child screen a back button named Wróć beside its title', async () => {
      const root = await renderAt('child', 'Moje konto', '/more');
      const back = root.querySelector<HTMLButtonElement>('button.shell-back');

      expect(back).not.toBeNull();
      expect(back!.getAttribute('aria-label')).toBe('Wróć');
      expect(root.querySelector('h1.shell-title')!.textContent!.trim()).toBe('Moje konto');
    });

    it('links the account screen from the bar on every signed-in screen', async () => {
      for (const [level, title] of [
        ['brand', 'Start'],
        ['tab', 'Zajęcia'],
      ] as const) {
        TestBed.resetTestingModule();
        const root = await renderAt(level, title);
        const profile = root.querySelector<HTMLAnchorElement>('a.shell-profile');

        expect(profile).not.toBeNull();
        expect(profile!.getAttribute('href')).toBe('/profile');
        expect(profile!.getAttribute('aria-label')).toBe('Moje konto');
      }
    });

    it('offers no profile link while signed out', async () => {
      const root = await renderAt('brand', 'Logowanie', null, null);

      expect(root.querySelector('.shell-profile')).toBeNull();
    });

    it('shows no title while signed out, whatever the route says', async () => {
      const root = await renderAt('tab', 'Grafik', null, null);

      expect(root.querySelector('.shell-title')).toBeNull();
    });
  });

  it('renders a bar with only Więcej for a blocked account', async () => {
    const root = await render(BLOCKED_MEMBER);

    expect(root.querySelector('.bottom-nav')).not.toBeNull();
    expect(barLabels(root)).toEqual(['Więcej']);
  });
});
