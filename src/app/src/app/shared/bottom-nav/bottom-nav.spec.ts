import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { NavLink, navigationFor } from '../../core/layout/navigation';
import { ScreenLevel } from '../../core/layout/screen';
import { ScreenTitle } from '../../core/layout/screen-title';
import { Up } from '../../core/layout/up';
import { BottomNav } from './bottom-nav';

@Component({ template: '' })
class Blank {}

const MEMBER_TABS = navigationFor('member', false).bar;

@Component({
  imports: [BottomNav],
  template: '<app-bottom-nav [links]="links()" />',
})
class Host {
  readonly links = signal<readonly NavLink[]>(MEMBER_TABS);
}

/**
 * The phone's primary navigation (S-12).
 *
 * Since S-25 the bar renders whatever links the shell hands it — the persona's, from
 * core/layout/navigation.ts, whose spec pins which links each persona gets. What stays here is what
 * the bar itself owns: order, names, icons, and the Start tab's exact match, because `/` is a prefix
 * of every other route and a prefix match would mark two tabs at once.
 */
describe('BottomNav', () => {
  async function create(url = '/', links: readonly NavLink[] = MEMBER_TABS) {
    TestBed.configureTestingModule({
      imports: [Host],
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

    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.links.set(links);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  function tabs(element: HTMLElement): HTMLAnchorElement[] {
    return [...element.querySelectorAll<HTMLAnchorElement>('.bottom-nav-tab')];
  }

  it('renders the links it is given, in order', async () => {
    const staff = navigationFor('trainer', false).bar;
    const element = await create('/', staff);

    expect(tabs(element).map((a) => a.getAttribute('aria-label'))).toEqual(
      staff.map((link) => link.label),
    );
    expect(tabs(element).map((a) => a.getAttribute('href'))).toEqual(
      staff.map((link) => link.route),
    );
  });

  /**
   * The tabs carry a VISIBLE label beside the icon, so what matters is that the accessible name and
   * the printed one AGREE (WCAG 2.5.3, Label in Name). The icon stays hidden — it would otherwise
   * announce the tab twice.
   */
  it('gives every tab an accessible name matching its visible label, and hides the icon', async () => {
    const element = await create();

    for (const tab of tabs(element)) {
      const visible = tab.querySelector('.bottom-nav-tab-label')!.textContent!.trim();

      expect(visible).toBeTruthy();
      expect(tab.getAttribute('aria-label')).toBe(visible);
      expect(tab.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
    }
  });

  it('marks the current tab with aria-current', async () => {
    const element = await create('/my-classes');
    const current = tabs(element).filter((a) => a.getAttribute('aria-current') === 'page');

    expect(current.length).toBe(1);
    expect(current[0].getAttribute('aria-label')).toBe('Zajęcia');
  });

  /** The exact-match case: without it Start would claim to be current on every route. */
  it('does not mark Start as current while on another route', async () => {
    const element = await create('/my-plan');
    const start = tabs(element)[0];

    expect(start.getAttribute('aria-label')).toBe('Start');
    expect(start.getAttribute('aria-current')).toBeNull();
  });

  it('marks Start as current on the dashboard', async () => {
    const element = await create('/');

    expect(tabs(element)[0].getAttribute('aria-current')).toBe('page');
  });
  /**
   * TABS DO NOT GROW HISTORY (mobile-native-feel): from Start a tab pushes, so Start stays under it;
   * from anywhere else it replaces; and the Start tab goes up, popping back to Start when it is below.
   */
  describe('history', () => {
    async function onScreen(level: ScreenLevel) {
      const element = await create(level === 'brand' ? '/' : '/my-plan');
      TestBed.inject(ScreenTitle).resolve('x', level, null);
      const router = TestBed.inject(Router);
      const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
      const up = vi.spyOn(TestBed.inject(Up), 'to').mockImplementation(() => undefined);
      return { element, navigate, up };
    }

    function tab(element: HTMLElement, label: string): HTMLAnchorElement {
      return tabs(element).find((a) => a.getAttribute('aria-label') === label)!;
    }

    it('pushes a tab opened from Start', async () => {
      const { element, navigate } = await onScreen('brand');

      tab(element, 'Zajęcia').click();

      expect(navigate).toHaveBeenCalledWith('/my-classes', { replaceUrl: false });
    });

    it('replaces the current entry when switching from one tab to another', async () => {
      const { element, navigate } = await onScreen('tab');

      tab(element, 'Zajęcia').click();

      expect(navigate).toHaveBeenCalledWith('/my-classes', { replaceUrl: true });
    });

    it('goes up to Start from the Start tab rather than stacking a second Start', async () => {
      const { element, navigate, up } = await onScreen('tab');

      tab(element, 'Start').click();

      expect(up).toHaveBeenCalledWith('/');
      expect(navigate).not.toHaveBeenCalled();
    });

    it('leaves a modified click to the browser', async () => {
      const { element, navigate } = await onScreen('tab');
      const click = new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true });

      tab(element, 'Zajęcia').dispatchEvent(click);

      expect(navigate).not.toHaveBeenCalled();
      expect(click.defaultPrevented).toBe(false);
    });
  });
});
