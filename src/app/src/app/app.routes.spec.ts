import { Route } from '@angular/router';
import { Persona } from './core/auth/persona';
import { navigationFor } from './core/layout/navigation';
import { ScreenData } from './core/layout/screen';
import { routes } from './app.routes';

/**
 * Every screen names itself and says how deep it sits (mobile-native-feel) — the phone's app bar,
 * document.title and the back behaviour all read it from here, so a route without it is a screen
 * with a blank bar.
 */
describe('route table', () => {
  const screens = routes.filter((route) => route.path !== '**');

  function data(route: Route): Partial<ScreenData> {
    return (route.data ?? {}) as Partial<ScreenData>;
  }

  it.each(screens.map((route) => [route.path ?? '', route] as const))(
    "'/%s' declares a title and a level",
    (_, route) => {
      expect(typeof route.title).toBe('string');
      expect(route.title).not.toBe('');
      expect(['brand', 'tab', 'child']).toContain(data(route).level);
    },
  );

  it('gives every child screen a parent it can go up to', () => {
    for (const route of screens.filter((r) => data(r).level === 'child')) {
      const parent = data(route).parent;

      expect(parent, route.path).toMatch(/^\//);
      expect(
        screens.map((r) => `/${r.path}`),
        route.path,
      ).toContain(parent);
    }
  });

  /**
   * THE BAR AND THE MENUS SAY ONE WORD (the S-25 one-table rule, extended). A destination's route
   * title is the label every menu prints for it, for every persona at both widths — otherwise the
   * tab reads "Grafik" and the bar above the screen it opens says something else.
   */
  it('titles every menu destination with its menu label', () => {
    const personas: (Persona | null)[] = ['member', 'trainer', 'admin', null];

    for (const persona of personas) {
      for (const desk of [true, false]) {
        const nav = navigationFor(persona, desk);

        for (const link of [...nav.header, ...nav.bar, ...nav.more]) {
          const route = screens.find((r) => `/${r.path}` === link.route);

          expect(route, link.route).toBeDefined();
          expect(route!.title, link.route).toBe(link.label);
          expect(['brand', 'tab'], link.route).toContain(data(route!).level);
        }
      }
    }
  });
});
