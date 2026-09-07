import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { CurrentUser } from '../../core/auth/auth.models';
import { More } from './more';

const ADMIN: CurrentUser = {
  id: 'a1',
  email: 'admin@test.local',
  displayName: 'Admin',
  status: 'Active',
  roles: ['User', 'Admin'],
};

const MEMBER: CurrentUser = { ...ADMIN, id: 'm1', displayName: 'Member', roles: ['User'] };
const TRAINER: CurrentUser = {
  ...ADMIN,
  id: 't1',
  displayName: 'Trainer',
  roles: ['User', 'Trainer'],
};
const PENDING_MEMBER: CurrentUser = { ...MEMBER, id: 'p1', status: 'Pending' };
const INACTIVE_ADMIN: CurrentUser = { ...ADMIN, id: 'a2', status: 'Pending' };
const INACTIVE_TRAINER: CurrentUser = { ...TRAINER, id: 't2', status: 'Pending' };

/** Every admin destination the panel offers, in template order. */
const ADMIN_HREFS = [
  '/admin/approvals',
  '/admin/members',
  '/admin/classes',
  '/admin/class-types',
  '/admin/exercises',
];

/**
 * The "Więcej" hub (S-12).
 *
 * THIS SPEC IS THE POINT OF THE SCREEN. The bottom bar carries five tabs that are identical for every
 * role, so every role-conditional link in the app now lives here — which means this one file is where
 * the whole visibility matrix is enforced. The S-01 implementation review found a shipped bug of
 * exactly this kind (an admin link tested isAdmin() alone, so a Pending admin saw a link that bounced
 * them), and the inactive-role cases below are here so it cannot happen again silently.
 */
describe('More', () => {
  function createWith(user: CurrentUser) {
    TestBed.configureTestingModule({
      imports: [More],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            user: () => user,
            isAuthenticated: () => true,
            isActive: () => user.status === 'Active',
            isAdmin: () => user.roles.includes('Admin'),
            isTrainer: () => user.roles.includes('Trainer'),
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

  it('always offers the profile screen and a logout button', () => {
    const element = createWith(MEMBER);

    expect(hrefs(element)).toContain('/profile');
    expect(element.querySelector('button')!.textContent).toContain('Wyloguj');
  });

  /**
   * The exception the whole screen is built around: /profile is gated on isAuthenticated(), NOT
   * isActive(), because a Pending member needs it to supply the contact details S-13 made mandatory.
   * Hiding it here would strand them once the header's links are gone on a phone.
   */
  it('still offers the profile screen to a member who is not yet approved', () => {
    const element = createWith(PENDING_MEMBER);

    expect(hrefs(element)).toContain('/profile');
  });

  it('shows no panel at all to a plain member', () => {
    const element = createWith(MEMBER);

    expect(element.textContent).not.toContain('Panel');
    for (const href of ADMIN_HREFS) {
      expect(hrefs(element)).not.toContain(href);
    }
    expect(hrefs(element)).not.toContain('/trainer/plans');
  });

  it('shows a trainer the plans entry and nothing else from the panel', () => {
    const element = createWith(TRAINER);

    expect(hrefs(element)).toContain('/trainer/plans');
    for (const href of ADMIN_HREFS) {
      expect(hrefs(element)).not.toContain(href);
    }
  });

  /** All six, including the four that had no navigation anywhere before this slice (deferred by S-10). */
  it('shows an admin every panel entry, plans included', () => {
    const element = createWith(ADMIN);

    for (const href of [...ADMIN_HREFS, '/trainer/plans']) {
      expect(hrefs(element)).toContain(href);
    }
  });

  /**
   * The S-01 F5 case. adminGuard is `isAdmin() && isActive()`; an admin whose account is not active
   * fails it, so showing the links would offer a trip to /admin/approvals -> / and back.
   */
  it('hides every panel entry from an admin whose account is not active', () => {
    const element = createWith(INACTIVE_ADMIN);

    expect(element.textContent).not.toContain('Panel');
    for (const href of ADMIN_HREFS) {
      expect(hrefs(element)).not.toContain(href);
    }
  });

  /** Same rule on the trainer side, matching trainerGuard's isActive() half. */
  it('hides the plans entry from a trainer whose account is not active', () => {
    const element = createWith(INACTIVE_TRAINER);

    expect(hrefs(element)).not.toContain('/trainer/plans');
  });
});
