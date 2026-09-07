import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { BOTTOM_NAV_TABS, BottomNav } from './bottom-nav';

@Component({ template: '' })
class Blank {}

/**
 * The phone's primary navigation (S-12).
 *
 * Two properties matter here and neither is cosmetic. The tab set must be IDENTICAL for every role —
 * that is what keeps a visibility matrix, and the S-01 F5 bug class with it, out of the bar. And the
 * Start tab must be exact-matched, because `/` is a prefix of every other route in the app: get that
 * wrong and every tab reads as active everywhere, which is worse than no marking at all.
 */
describe('BottomNav', () => {
  async function create(url = '/') {
    TestBed.configureTestingModule({
      imports: [BottomNav],
      providers: [
        provideRouter([
          { path: '', component: Blank },
          { path: 'schedule', component: Blank },
          { path: 'my-classes', component: Blank },
          { path: 'my-plan', component: Blank },
          { path: 'more', component: Blank },
        ]),
      ],
    });

    await TestBed.inject(Router).navigateByUrl(url);

    const fixture = TestBed.createComponent(BottomNav);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  function tabs(element: HTMLElement): HTMLAnchorElement[] {
    return [...element.querySelectorAll<HTMLAnchorElement>('.bottom-nav-tab')];
  }

  it('renders exactly the five declared tabs, in order', async () => {
    const element = await create();

    expect(tabs(element).map((a) => a.textContent?.trim())).toEqual([
      'Start',
      'Grafik',
      'Moje zajęcia',
      'Mój plan',
      'Więcej',
    ]);
    expect(tabs(element).length).toBe(BOTTOM_NAV_TABS.length);
  });

  /** Role-blind by construction: the component injects no AuthService and takes no inputs. */
  it('carries no role-conditional destination', async () => {
    const element = await create();
    const hrefs = tabs(element).map((a) => a.getAttribute('href'));

    for (const href of hrefs) {
      expect(href).not.toContain('/admin');
      expect(href).not.toContain('/trainer');
    }
  });

  it('marks the current tab with aria-current', async () => {
    const element = await create('/my-classes');
    const current = tabs(element).filter((a) => a.getAttribute('aria-current') === 'page');

    expect(current.length).toBe(1);
    expect(current[0].textContent?.trim()).toBe('Moje zajęcia');
  });

  /**
   * The exact-match case. `/` prefixes every route, so without routerLinkActiveOptions the Start tab
   * would claim to be current on /schedule too — and the bar would mark two tabs at once.
   */
  it('does not mark Start as current while on another route', async () => {
    const element = await create('/schedule');
    const start = tabs(element)[0];

    expect(start.textContent?.trim()).toBe('Start');
    expect(start.getAttribute('aria-current')).toBeNull();
  });

  it('marks Start as current on the dashboard', async () => {
    const element = await create('/');

    expect(tabs(element)[0].getAttribute('aria-current')).toBe('page');
  });
});
