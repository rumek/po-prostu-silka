import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { HomePlaceholder, LoginPlaceholder } from './core/auth/route-placeholders';

/**
 * The route structure S-01 slots real screens into. Both components here are stubs - see
 * core/auth/route-placeholders.ts.
 *
 * The shape is what matters: one unauthenticated route (the guard's redirect target) and one
 * guarded route, so the guard and interceptor are actually exercised rather than shipped untested.
 */
export const routes: Routes = [
  { path: 'login', component: LoginPlaceholder },
  { path: '', component: HomePlaceholder, canActivate: [authGuard] },
  { path: '**', redirectTo: '' },
];
