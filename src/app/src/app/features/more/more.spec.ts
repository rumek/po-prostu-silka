import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { CurrentUser } from '../../core/auth/auth.models';
import { More } from './more';

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
const ADMIN = user('admin', ['Admin']);
const ADMIN_TRAINER = user('admin-trainer', ['Admin', 'Trainer']);
const BLOCKED_ADMIN = user('blocked-admin', ['Admin'], { status: 'Blocked' });
const BLOCKED_MEMBER = user('blocked-member', ['User'], { membershipStatus: 'Blocked' });

/**
 * The "Więcej" hub (S-12). Since S-25 its panel is the persona's `more` list from
 * core/layout/navigation.ts: only the admin overflows the five-slot bar (Typy zajęć). Every account
 * — a blocked one included — keeps Moje konto and logout, because on a phone this screen is the only
 * path to either.
 */
describe('More', () => {
  function createWith(current: CurrentUser) {
    TestBed.configureTestingModule({
      imports: [More],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            user: () => current,
            isAuthenticated: () => true,
            logout: () => Promise.resolve(),
          } as unknown as AuthService,
        },
      ],
    });

    const fixture = TestBed.createComponent(More);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function hrefs(element: HTMLElement): string[] {
    return [...element.querySelectorAll('a')].map((a) => a.getAttribute('href') ?? '');
  }

  it.each([
    ['a member', MEMBER],
    ['a trainer', TRAINER],
    ['an admin', ADMIN],
    ['a blocked member', BLOCKED_MEMBER],
  ])('always offers %s the profile screen and a logout button', (_, current) => {
    const element = createWith(current);

    expect(hrefs(element)).toContain('/profile');
    expect(element.querySelector('button')!.textContent).toContain('Wyloguj');
  });

  it('gives an admin Typy zajęć as the only panel entry', () => {
    const element = createWith(ADMIN);

    expect(element.textContent).toContain('Panel');
    expect(hrefs(element)).toEqual(['/profile', '/admin/class-types']);
  });

  it('gives an admin who also trains the same panel as an admin', () => {
    expect(hrefs(createWith(ADMIN_TRAINER))).toEqual(['/profile', '/admin/class-types']);
  });

  it.each([
    ['a member', MEMBER],
    ['a trainer', TRAINER],
  ])('shows no panel to %s — their whole menu fits the bar', (_, current) => {
    const element = createWith(current);

    expect(element.textContent).not.toContain('Panel');
    expect(hrefs(element)).toEqual(['/profile']);
  });

  /** The S-01 F5 case: no persona, no panel — a link here would bounce off adminGuard. */
  it('shows a blocked admin the account and logout only', () => {
    const element = createWith(BLOCKED_ADMIN);

    expect(element.textContent).not.toContain('Panel');
    expect(hrefs(element)).toEqual(['/profile']);
  });
});
