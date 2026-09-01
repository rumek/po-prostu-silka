import { Routes } from '@angular/router';
import { activeMemberGuard } from './core/auth/active-member.guard';
import { adminGuard } from './core/auth/admin.guard';
import { authGuard } from './core/auth/auth.guard';
import { Approvals } from './features/admin/approvals/approvals';
import { Members } from './features/admin/members/members';
import { Login } from './features/auth/login/login';
import { Pending } from './features/auth/pending/pending';
import { Register } from './features/auth/register/register';
import { Home } from './features/home/home';

/**
 * Paths stay English while the copy is Polish (S-01 D10): /login already exists and authGuard
 * redirects to it, and every identifier in the codebase is English already.
 *
 * The guards compose rather than nest (D12). authGuard answers "is there a session"; activeMemberGuard
 * answers "is it approved". /pending carries only the first — a Pending member must be able to reach
 * the screen that exists for them, and listing activeMemberGuard there would redirect it to itself.
 */
export const routes: Routes = [
  { path: 'login', component: Login },
  { path: 'register', component: Register },
  { path: 'pending', component: Pending, canActivate: [authGuard] },
  { path: '', component: Home, canActivate: [authGuard, activeMemberGuard] },
  { path: 'admin/approvals', component: Approvals, canActivate: [authGuard, adminGuard] },
  { path: 'admin/members', component: Members, canActivate: [authGuard, adminGuard] },
  { path: '**', redirectTo: '' },
];
