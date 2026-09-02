import { Routes } from '@angular/router';
import { activeMemberGuard } from './core/auth/active-member.guard';
import { adminGuard } from './core/auth/admin.guard';
import { authGuard } from './core/auth/auth.guard';
import { Approvals } from './features/admin/approvals/approvals';
import { Classes } from './features/admin/classes/classes';
import { ClassForm } from './features/admin/classes/class-form';
import { ClassTypes } from './features/admin/class-types/class-types';
import { ClassTypeForm } from './features/admin/class-types/class-type-form';
import { Members } from './features/admin/members/members';
import { Schedule } from './features/schedule/schedule';
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
  { path: 'schedule', component: Schedule, canActivate: [authGuard, activeMemberGuard] },
  { path: 'admin/classes', component: Classes, canActivate: [authGuard, adminGuard] },
  // 'new' MUST precede ':id', or the literal segment is swallowed by the parameter.
  { path: 'admin/classes/new', component: ClassForm, canActivate: [authGuard, adminGuard] },
  { path: 'admin/classes/:id', component: ClassForm, canActivate: [authGuard, adminGuard] },
  { path: 'admin/class-types', component: ClassTypes, canActivate: [authGuard, adminGuard] },
  // 'new' MUST precede ':id' here too, or the literal segment is swallowed by the parameter.
  { path: 'admin/class-types/new', component: ClassTypeForm, canActivate: [authGuard, adminGuard] },
  { path: 'admin/class-types/:id', component: ClassTypeForm, canActivate: [authGuard, adminGuard] },
  { path: '**', redirectTo: '' },
];
