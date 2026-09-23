import { ActivatedRouteSnapshot, Route } from '@angular/router';
import { ScreenData } from './screen';
import { transitionKind } from './view-transitions';

const START: Route = { path: '', data: { level: 'brand' } satisfies ScreenData };
const CLASSES: Route = { path: 'my-classes', data: { level: 'tab' } satisfies ScreenData };
const PLAN: Route = { path: 'my-plan', data: { level: 'tab' } satisfies ScreenData };
const EXERCISE: Route = {
  path: 'my-plan/exercises/:id',
  data: { level: 'child', parent: '/my-plan' } satisfies ScreenData,
};

/** A root snapshot with one routed leaf under it — the shape the router hands the callback. */
function at(route: Route, params: Record<string, string> = {}): ActivatedRouteSnapshot {
  const leaf = { routeConfig: route, params, data: route.data ?? {}, firstChild: null };
  return {
    routeConfig: null,
    params: {},
    data: {},
    firstChild: leaf,
  } as unknown as ActivatedRouteSnapshot;
}

/** Which motion a navigation gets (mobile-native-feel), row by row of the plan's table. */
describe('transitionKind', () => {
  it('skips a query-only change on the same screen', () => {
    expect(transitionKind(at(PLAN), at(PLAN), 'imperative')).toBe('skip');
  });

  it('does not skip the same screen with a different id', () => {
    expect(transitionKind(at(EXERCISE, { id: '1' }), at(EXERCISE, { id: '2' }), 'imperative')).toBe(
      'forward',
    );
  });

  it('fades between two tabs', () => {
    expect(transitionKind(at(CLASSES), at(PLAN), 'imperative')).toBe('fade');
  });

  it('fades from Start to a tab and back', () => {
    expect(transitionKind(at(START), at(PLAN), 'imperative')).toBe('fade');
    expect(transitionKind(at(PLAN), at(START), 'imperative')).toBe('fade');
  });

  it('slides forward into a child', () => {
    expect(transitionKind(at(PLAN), at(EXERCISE, { id: '1' }), 'imperative')).toBe('forward');
  });

  it('slides back up from a child to its tab', () => {
    expect(transitionKind(at(EXERCISE, { id: '1' }), at(PLAN), 'imperative')).toBe('back');
  });

  it('slides back for the browser back button, even between two tabs', () => {
    expect(transitionKind(at(CLASSES), at(PLAN), 'popstate')).toBe('back');
    expect(transitionKind(at(PLAN), at(START), 'popstate')).toBe('back');
  });
});
