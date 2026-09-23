import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { ScreenData } from './screen';
import { ScreenTitle, ScreenTitleStrategy } from './screen-title';

@Component({ template: '' })
class Blank {}

/**
 * The shell's source for "what is this screen and how deep is it" (mobile-native-feel). The routes
 * here are NESTED on purpose: the app's are flat, but the router hands the strategy the root
 * snapshot either way, and a strategy that read the root's data would call every screen a brand one.
 */
describe('ScreenTitle', () => {
  async function setup(url: string) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: '',
            title: 'Start',
            data: { level: 'brand' } satisfies ScreenData,
            component: Blank,
          },
          {
            path: 'area',
            children: [
              {
                path: 'list',
                title: 'Lista',
                data: { level: 'tab' } satisfies ScreenData,
                component: Blank,
              },
              {
                path: 'list/:id',
                title: 'Pozycja',
                data: { level: 'child', parent: '/area/list' } satisfies ScreenData,
                component: Blank,
              },
            ],
          },
        ]),
        { provide: TitleStrategy, useClass: ScreenTitleStrategy },
      ],
    });

    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    return { router, screen: TestBed.inject(ScreenTitle) };
  }

  it('publishes the leaf route title, level and parent', async () => {
    const { screen } = await setup('/area/list/7');

    expect(screen.title()).toBe('Pozycja');
    expect(screen.level()).toBe('child');
    expect(screen.parent()).toBe('/area/list');
  });

  it('names a titled screen and the app in document.title', async () => {
    await setup('/area/list');

    expect(document.title).toBe('Lista · Po Prostu Siłka');
  });

  it('gives a brand screen the app name alone', async () => {
    const { screen } = await setup('/');

    expect(screen.level()).toBe('brand');
    expect(screen.parent()).toBeNull();
    expect(document.title).toBe('Po Prostu Siłka');
  });

  it('lets a screen override its title until the next navigation', async () => {
    const { router, screen } = await setup('/area/list/7');

    screen.set('Przysiad');
    expect(screen.title()).toBe('Przysiad');
    expect(document.title).toBe('Przysiad · Po Prostu Siłka');

    await router.navigateByUrl('/area/list');
    expect(screen.title()).toBe('Lista');
    expect(document.title).toBe('Lista · Po Prostu Siłka');
  });
  /** Same screen, new query (a search, a page): the data that named it is still on screen. */
  it('keeps the override across a query-only navigation', async () => {
    const { router, screen } = await setup('/area/list/7');

    screen.set('Przysiad');
    await router.navigateByUrl('/area/list/7?page=2');

    expect(screen.title()).toBe('Przysiad');
    expect(document.title).toBe('Przysiad · Po Prostu Siłka');
  });

  it('drops the override when only the id changes, because that is a different record', async () => {
    const { router, screen } = await setup('/area/list/7');

    screen.set('Przysiad');
    await router.navigateByUrl('/area/list/8');

    expect(screen.title()).toBe('Pozycja');
  });
});
