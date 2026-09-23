import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd, Router, provideRouter } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { Up } from './up';

@Component({ template: '' })
class Blank {}

/**
 * "Up" pops when the target is below this screen in this session's history, and otherwise replaces
 * this screen with it (mobile-native-feel). The history is SpyLocation's, which the router writes
 * to and reads popstates from exactly as it does the browser's.
 */
describe('Up', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: '', component: Blank },
          { path: 'my-plan', component: Blank },
          { path: 'my-plan/exercises/:id', component: Blank },
          { path: 'my-classes', component: Blank },
          { path: 'more', component: Blank },
        ]),
        provideLocationMocks(),
      ],
    });

    const up = TestBed.inject(Up);
    const router = TestBed.inject(Router);
    const location = TestBed.inject(Location);
    // What bootstrap's initial navigation does in the app; TestBed never runs it.
    router.setUpLocationChangeListener();
    return { up, router, location };
  }

  it('pops back to the parent the child was opened from', async () => {
    const { up, router, location } = setup();
    await router.navigateByUrl('/');
    await router.navigateByUrl('/my-plan');
    await router.navigateByUrl('/my-plan/exercises/7');
    const go = vi.spyOn(location, 'historyGo');
    const navigate = vi.spyOn(router, 'navigateByUrl');

    up.to('/my-plan');

    expect(go).toHaveBeenCalledWith(-1);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('replaces a deep-linked child with its parent, so back from the parent leaves', async () => {
    const { up, router, location } = setup();
    await router.navigateByUrl('/my-plan/exercises/7');
    const go = vi.spyOn(location, 'historyGo');
    const navigate = vi.spyOn(router, 'navigateByUrl');

    up.to('/my-plan');

    expect(go).not.toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/my-plan', { replaceUrl: true });
  });

  it('pops past every screen above the target, not just one', async () => {
    const { up, router } = setup();
    await router.navigateByUrl('/');
    await router.navigateByUrl('/my-plan');
    await router.navigateByUrl('/my-plan/exercises/7');

    expect(up.stepsBackTo('/')).toBe(2);
  });

  it('counts a replaced entry as the screen that replaced it', async () => {
    const { up, router } = setup();
    await router.navigateByUrl('/');
    await router.navigateByUrl('/my-plan');
    await router.navigateByUrl('/my-classes', { replaceUrl: true });

    expect(up.stepsBackTo('/')).toBe(1);
    expect(up.stepsBackTo('/my-plan')).toBe(0);
  });

  it('matches the target by path, whatever query the entry below carried', async () => {
    const { up, router } = setup();
    await router.navigateByUrl('/my-plan?page=2');
    await router.navigateByUrl('/my-plan/exercises/7');

    expect(up.stepsBackTo('/my-plan')).toBe(1);
  });

  it('follows the browser back button through its model', async () => {
    const { up, router, location } = setup();
    await router.navigateByUrl('/');
    await router.navigateByUrl('/my-plan');
    await router.navigateByUrl('/my-plan/exercises/7');

    const ended = firstValueFrom(router.events.pipe(filter((e) => e instanceof NavigationEnd)));
    location.back();
    await ended;

    expect(router.url).toBe('/my-plan');
    expect(up.stepsBackTo('/')).toBe(1);
    expect(up.stepsBackTo('/my-plan/exercises/7')).toBe(0);
  });
});
