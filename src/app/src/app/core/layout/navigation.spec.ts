import { Persona } from '../auth/persona';
import { NavLink, navigationFor } from './navigation';

const routes = (links: readonly NavLink[]) => links.map((link) => link.route);

/**
 * The per-persona link table (S-25). The exact lists are pinned by hand — a spec deriving them from
 * the table would bless whatever the table says.
 */
describe('navigationFor', () => {
  it('gives a member their classes and plan, and no schedule', () => {
    const nav = navigationFor('member', true);

    expect(routes(nav.header)).toEqual(['/', '/my-classes', '/my-plan']);
    expect(routes(nav.bar)).toEqual(['/', '/my-classes', '/my-plan', '/more']);
    expect(nav.more).toEqual([]);
  });

  it('gives a trainer the schedule and their member list, and no plan or classes of their own', () => {
    const nav = navigationFor('trainer', true);

    expect(routes(nav.header)).toEqual(['/', '/schedule', '/trainer/members']);
    expect(routes(nav.bar)).toEqual(['/', '/schedule', '/trainer/members', '/more']);
    expect(nav.more).toEqual([]);
  });

  it('gives an admin at desk width the calendar, the admin lists and Typy zajęć under Więcej', () => {
    const nav = navigationFor('admin', true);

    expect(routes(nav.header)).toEqual([
      '/',
      '/admin/classes',
      '/admin/members',
      '/admin/class-types',
      '/admin/exercises',
    ]);
    expect(routes(nav.bar)).toEqual([
      '/',
      '/admin/classes',
      '/admin/members',
      '/admin/exercises',
      '/more',
    ]);
    expect(routes(nav.more)).toEqual(['/admin/class-types']);
  });

  /** UX-01: below desk the calendar refuses to render, so Grafik is the schedule there. */
  it("flips the admin's Grafik to the schedule below desk width", () => {
    const nav = navigationFor('admin', false);

    expect(nav.header[1]).toMatchObject({ route: '/schedule', label: 'Grafik' });
    expect(nav.bar[1]).toMatchObject({ route: '/schedule', label: 'Grafik' });
  });

  /**
   * The shell renders Moje konto and logout for every session; on a phone /more is the only way to
   * them, so the bar keeps Więcej even with no persona.
   */
  it('gives no persona only the Więcej tab', () => {
    const nav = navigationFor(null, true);

    expect(nav.header).toEqual([]);
    expect(routes(nav.bar)).toEqual(['/more']);
    expect(nav.more).toEqual([]);
  });

  describe.each(['member', 'trainer', 'admin'] as Persona[])('for %s', (persona) => {
    it.each([true, false])('reaches every header link from the phone (desk=%s)', (desk) => {
      const nav = navigationFor(persona, desk);
      const phone = new Set([...routes(nav.bar), ...routes(nav.more)]);

      for (const route of routes(nav.header)) {
        expect(phone).toContain(route);
      }
    });

    it('never gives the bar more than five tabs, each with its own icon', () => {
      const bar = navigationFor(persona, false).bar;

      expect(bar.length).toBeLessThanOrEqual(5);
      expect(new Set(bar.map((link) => link.icon)).size).toBe(bar.length);
    });

    it('matches only Start exactly', () => {
      const nav = navigationFor(persona, true);

      for (const link of [...nav.header, ...nav.bar, ...nav.more]) {
        expect(link.exact).toBe(link.route === '/');
      }
    });
  });

  /** The S-25 rule the menu exists to enforce: no persona is offered another persona's screens. */
  it('never offers staff the member screens, nor a member the staff ones', () => {
    for (const desk of [true, false]) {
      const member = navigationFor('member', desk);
      const memberRoutes = [...member.header, ...member.bar, ...member.more].map((l) => l.route);
      expect(memberRoutes.some((r) => r.startsWith('/admin') || r.startsWith('/trainer'))).toBe(
        false,
      );
      expect(memberRoutes).not.toContain('/schedule');

      for (const persona of ['trainer', 'admin'] as Persona[]) {
        const staff = navigationFor(persona, desk);
        const staffRoutes = [...staff.header, ...staff.bar, ...staff.more].map((l) => l.route);
        expect(staffRoutes).not.toContain('/my-classes');
        expect(staffRoutes).not.toContain('/my-plan');
      }
    }
  });
});
