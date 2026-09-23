import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';
import { memberGuard } from '../../core/auth/persona.guards';
import { MyClasses } from './my-classes';

/**
 * The two tabs on "Moje zajęcia" (S-27): in the URL, replacing rather than pushing history, ARIA
 * tabs with arrow keys, and a history that is fetched only once its tab is first shown.
 */
describe('MyClasses — tabs', () => {
  let harness: RouterTestingHarness;
  let controller: HttpTestingController;

  async function open(url: string): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: 'my-classes', component: MyClasses }]),
      ],
    });

    controller = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    controller.expectOne('/api/bookings/mine').flush([]);
    await settle();
  }

  afterEach(() => controller.verify());

  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  function element(): HTMLElement {
    return harness.routeNativeElement!;
  }

  function tab(name: string): HTMLButtonElement {
    return [...element().querySelectorAll<HTMLButtonElement>('[role="tab"]')].find((t) =>
      t.textContent?.includes(name),
    )!;
  }

  function panel(id: string): HTMLElement {
    return element().querySelector<HTMLElement>(`#${id}`)!;
  }

  const EMPTY_HISTORY = { summary: null, items: [], earlierBefore: null };

  it('opens on the upcoming tab and fetches no history', async () => {
    await open('/my-classes');

    expect(tab('Nadchodzące').getAttribute('aria-selected')).toBe('true');
    expect(tab('Historia').getAttribute('aria-selected')).toBe('false');
    expect(panel('panel-history').hidden).toBe(true);
    controller.expectNone('/api/bookings/history');
  });

  it('selects the history tab from the URL, so a reload keeps it', async () => {
    await open('/my-classes?widok=historia');

    expect(tab('Historia').getAttribute('aria-selected')).toBe('true');
    expect(panel('panel-upcoming').hidden).toBe(true);

    controller.expectOne('/api/bookings/history').flush(EMPTY_HISTORY);
    await settle();

    expect(element().textContent).toContain('Nie masz jeszcze zajęć w historii');
  });

  it('switches by replacing the URL, not by pushing a history entry', async () => {
    await open('/my-classes');
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');

    tab('Historia').click();
    await settle();

    expect(navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({ queryParams: { widok: 'historia' }, replaceUrl: true }),
    );
    expect(TestBed.inject(Location).path()).toBe('/my-classes?widok=historia');

    controller.expectOne('/api/bookings/history').flush(EMPTY_HISTORY);
    await settle();
  });

  it('fetches the history once, however often the tabs flip', async () => {
    await open('/my-classes');

    tab('Historia').click();
    await settle();
    controller.expectOne('/api/bookings/history').flush(EMPTY_HISTORY);
    await settle();

    tab('Nadchodzące').click();
    await settle();
    tab('Historia').click();
    await settle();

    controller.expectNone('/api/bookings/history');
    expect(TestBed.inject(Location).path()).toBe('/my-classes?widok=historia');
  });

  it('moves between the tabs with the arrow keys, and only the selected tab is tabbable', async () => {
    await open('/my-classes');

    expect(tab('Nadchodzące').tabIndex).toBe(0);
    expect(tab('Historia').tabIndex).toBe(-1);

    tab('Nadchodzące').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    await settle();

    expect(tab('Historia').getAttribute('aria-selected')).toBe('true');
    expect(tab('Historia').tabIndex).toBe(0);
    expect(document.activeElement).toBe(tab('Historia'));

    controller.expectOne('/api/bookings/history').flush(EMPTY_HISTORY);
    await settle();

    tab('Historia').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    await settle();

    expect(tab('Nadchodzące').getAttribute('aria-selected')).toBe('true');
  });

  it('keeps the screen identity: h1 "Moje zajęcia", route title "Zajęcia"', async () => {
    await open('/my-classes');

    expect(element().querySelector('h1.screen-title')!.textContent).toContain('Moje zajęcia');
    expect(routes.find((r) => r.path === 'my-classes')!.title).toBe('Zajęcia');
  });

  /**
   * S-25: staff never even REQUEST member data. The history is under /my-classes, and that route is
   * memberGuarded, so a staff account never reaches the component that fetches it.
   */
  it('is reachable by members only, so staff never request the history', () => {
    TestBed.configureTestingModule({});

    expect(routes.find((r) => r.path === 'my-classes')!.canActivate).toContain(memberGuard);
  });
});
