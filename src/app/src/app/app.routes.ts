import { Routes } from '@angular/router';
import { activeMemberGuard } from './core/auth/active-member.guard';
import { adminGuard } from './core/auth/admin.guard';
import { authGuard } from './core/auth/auth.guard';
import { trainerGuard } from './core/auth/trainer.guard';
import { Approvals } from './features/admin/approvals/approvals';
import { ClassForm } from './features/admin/classes/class-form';
import { ClassTypes } from './features/admin/class-types/class-types';
import { ClassTypeForm } from './features/admin/class-types/class-type-form';
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
  // LAZY, both of them, and deliberately (S-07). They are the only routes that pull in
  // angular-calendar plus date-fns and its two drag/resize peers; eagerly loaded that lands in the
  // initial bundle, which already sits at ~424 kB against a 500 kB budget. It also means login,
  // register and the pending screen — everything an unapproved member ever sees — never download a
  // calendar.
  {
    path: 'schedule',
    loadComponent: () => import('./features/schedule/schedule').then((m) => m.Schedule),
    canActivate: [authGuard, activeMemberGuard],
  },
  // LAZY TOO, but for the opposite reason: /my-classes must not pull the calendar in, and loading it
  // eagerly beside routes that do is how it eventually would. It is a plain list by design (FR-010).
  {
    path: 'my-classes',
    loadComponent: () => import('./features/my-classes/my-classes').then((m) => m.MyClasses),
    canActivate: [authGuard, activeMemberGuard],
  },
  {
    path: 'admin/classes',
    loadComponent: () => import('./features/admin/classes/classes').then((m) => m.Classes),
    canActivate: [authGuard, adminGuard],
  },
  // 'new' MUST precede ':id', or the literal segment is swallowed by the parameter.
  { path: 'admin/classes/new', component: ClassForm, canActivate: [authGuard, adminGuard] },
  { path: 'admin/classes/:id', component: ClassForm, canActivate: [authGuard, adminGuard] },
  { path: 'admin/class-types', component: ClassTypes, canActivate: [authGuard, adminGuard] },
  // 'new' MUST precede ':id' here too, or the literal segment is swallowed by the parameter.
  { path: 'admin/class-types/new', component: ClassTypeForm, canActivate: [authGuard, adminGuard] },
  { path: 'admin/class-types/:id', component: ClassTypeForm, canActivate: [authGuard, adminGuard] },
  // S-10's exercise library. LAZY, and not for the reason the two routes above are: these screens
  // pull in nothing heavy. Eagerly loaded they still cost ~28 kB, which took the initial bundle from
  // 475 kB to 502.88 kB - past the 500 kB budget in angular.json, so `npm run build` started warning.
  // Lazy chunks keep the budget green and cost nothing an admin will notice.
  {
    path: 'admin/exercises',
    loadComponent: () => import('./features/admin/exercises/exercises').then((m) => m.Exercises),
    canActivate: [authGuard, adminGuard],
  },
  // 'new' MUST precede ':id' here too, or the literal segment is swallowed by the parameter.
  {
    path: 'admin/exercises/new',
    loadComponent: () =>
      import('./features/admin/exercises/exercise-form').then((m) => m.ExerciseForm),
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/exercises/:id/edit',
    loadComponent: () =>
      import('./features/admin/exercises/exercise-form').then((m) => m.ExerciseForm),
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/exercises/:id',
    loadComponent: () =>
      import('./features/admin/exercises/exercise-detail').then((m) => m.ExerciseDetail),
    canActivate: [authGuard, adminGuard],
  },
  // S-11's training plans, on two surfaces with two guards. LAZY for the exercise-library reason:
  // the initial bundle sits close to the 500 kB budget, and the builder additionally pulls in
  // @angular/cdk's drag-drop - which must land in the builder's own chunk and nowhere else.
  {
    path: 'trainer/plans',
    loadComponent: () => import('./features/trainer/plans/plans').then((m) => m.Plans),
    canActivate: [authGuard, trainerGuard],
  },
  // 'new' MUST precede ':id' here too, or the literal segment is swallowed by the parameter.
  {
    path: 'trainer/plans/new',
    loadComponent: () => import('./features/trainer/plans/plan-builder').then((m) => m.PlanBuilder),
    canActivate: [authGuard, trainerGuard],
  },
  {
    path: 'trainer/plans/:id',
    loadComponent: () => import('./features/trainer/plans/plan-builder').then((m) => m.PlanBuilder),
    canActivate: [authGuard, trainerGuard],
  },
  // The member's own plan. activeMemberGuard, not trainerGuard: every approved account has a plan
  // surface, the trainer's own included - the API applies ActiveMember at this group.
  {
    path: 'my-plan',
    loadComponent: () => import('./features/my-plan/my-plan').then((m) => m.MyPlan),
    canActivate: [authGuard, activeMemberGuard],
  },
  {
    path: 'my-plan/exercises/:id',
    loadComponent: () =>
      import('./features/my-plan/plan-exercise-detail').then((m) => m.PlanExerciseDetail),
    canActivate: [authGuard, activeMemberGuard],
  },
  // S-13's profile screen. authGuard ONLY, never activeMemberGuard — the API's /api/profile group
  // makes the same choice for the same reason: an account created before S-13 has no contact
  // details, and it must be able to supply them while still awaiting approval.
  //
  // LAZY like its neighbours: the initial bundle sits close to the 500 kB budget in angular.json,
  // and a screen most members open twice has no business in it.
  {
    path: 'profile',
    loadComponent: () => import('./features/profile/profile').then((m) => m.Profile),
    canActivate: [authGuard],
  },
  { path: '**', redirectTo: '' },
];
