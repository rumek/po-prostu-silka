import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SwPush } from '@angular/service-worker';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';
import { CurrentUser } from './core/auth/auth.models';

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

  it('renders a bar with only Więcej for a blocked account', async () => {
    const root = await render(BLOCKED_MEMBER);

    expect(root.querySelector('.bottom-nav')).not.toBeNull();
    expect(barLabels(root)).toEqual(['Więcej']);
  });
});
