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
// BLOCKED, not Pending — S-16 retired the pending state, and blocked is now the only way an account
// can be authenticated and still fail isActive().
const INACTIVE_ADMIN: CurrentUser = { ...ADMIN, id: 'a2', status: 'Blocked' };
const INACTIVE_TRAINER: CurrentUser = { ...TRAINER, id: 't2', status: 'Blocked' };

/** An owner who teaches (prd-v2 FR-003) — the one account `!isAdmin()` changes anything for. */
const ADMIN_TRAINER: CurrentUser = {
  ...ADMIN,
  id: 'a3',
  displayName: 'Admin Trainer',
  roles: ['User', 'Admin', 'Trainer'],
};

/** The trainer's member list (S-22), which replaced "Plany" → /trainer/plans. */
const TRAINER_MEMBERS = '/trainer/members';

/** Every admin destination the panel offers, in template order. */
const ADMIN_HREFS = ['/admin/members', '/admin/classes', '/admin/class-types', '/admin/exercises'];

/**
 * The "Więcej" hub (S-12).
 *
 * THIS SPEC IS THE POINT OF THE SCREEN. The bottom bar carries five tabs that are identical for every
 * role, so every role-conditional link in the app now lives here — which means this one file is where
 * the whole visibility matrix is enforced. The S-01 implementation review found a shipped bug of
 * exactly this kind (an admin link tested isAdmin() alone, so an admin who could not use the club saw
 * a link that bounced them), and the inactive-role cases below are here so it cannot happen again
 * silently.
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
   * isActive(), because an account still needs it to supply the contact details S-13 made mandatory.
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
    expect(hrefs(element)).not.toContain(TRAINER_MEMBERS);
  });

  /** S-22: a trainer reaches plans through their member list — the panel's only entry for them. */
  it('shows a trainer Członkowie → /trainer/members and nothing else from the panel', () => {
    const element = createWith(TRAINER);

    const link = element.querySelector<HTMLAnchorElement>(`a[href="${TRAINER_MEMBERS}"]`);
    expect(link?.textContent?.trim()).toBe('Członkowie');
    expect(hrefs(element)).not.toContain('/trainer/plans');
    for (const href of ADMIN_HREFS) {
      expect(hrefs(element)).not.toContain(href);
    }
  });

  /**
   * The four admin entries, and since S-22 no "Plany" and no trainer member list: an admin reaches a
   * plan through /admin/members, so a second member list would be a second path to the same place.
   */
  it('shows an admin every admin entry, and no trainer member list', () => {
    const element = createWith(ADMIN);

    for (const href of ADMIN_HREFS) {
      expect(hrefs(element)).toContain(href);
    }
    expect(hrefs(element)).not.toContain('/trainer/plans');
    expect(hrefs(element)).not.toContain(TRAINER_MEMBERS);
  });

  /**
   * isTrainerOnly() is STRICTER than trainerGuard, and this is the only account for whom the extra
   * `!isAdmin()` does anything — so it is the case that has to be pinned.
   */
  it('shows an admin who also trains the admin member list and not the trainer one', () => {
    const element = createWith(ADMIN_TRAINER);

    expect(hrefs(element)).toContain('/admin/members');
    expect(hrefs(element)).not.toContain(TRAINER_MEMBERS);
  });

  /**
   * The S-01 F5 case. adminGuard is `isAdmin() && isActive()`; an admin whose account is not active
   * fails it, so showing the links would offer a trip to /admin/members -> / and back.
   */
  it('hides every panel entry from an admin whose account is not active', () => {
    const element = createWith(INACTIVE_ADMIN);

    expect(element.textContent).not.toContain('Panel');
    for (const href of ADMIN_HREFS) {
      expect(hrefs(element)).not.toContain(href);
    }
  });

  /** Same rule on the trainer side, matching trainerGuard's isActive() half. */
  it('hides the member-list entry from a trainer whose account is not active', () => {
    const element = createWith(INACTIVE_TRAINER);

    expect(hrefs(element)).not.toContain(TRAINER_MEMBERS);
  });
});
