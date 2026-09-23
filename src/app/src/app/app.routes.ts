import { Routes } from '@angular/router';
import { activeMemberGuard } from './core/auth/active-member.guard';
import { adminGuard } from './core/auth/admin.guard';
import { authGuard } from './core/auth/auth.guard';
import { invitationGuard } from './core/auth/invitation.guard';
import { memberGuard, staffGuard } from './core/auth/persona.guards';
import { trainerGuard } from './core/auth/trainer.guard';
import { ClassForm } from './features/admin/classes/class-form';
import { ClassTypes } from './features/admin/class-types/class-types';
import { ClassTypeForm } from './features/admin/class-types/class-type-form';
import { Members } from './features/admin/members/members';
import { Login } from './features/auth/login/login';
import { Register } from './features/auth/register/register';
import { Dashboard } from './features/dashboard/dashboard';
import { ScreenData } from './core/layout/screen';

/**
 * Paths stay English while the copy is Polish (S-01 D10): /login already exists and authGuard
 * redirects to it, and every identifier in the codebase is English already.
 *
 * The guards compose rather than nest (D12). authGuard answers "is there a session";
 * activeMemberGuard answers "may this person use the club"; memberGuard and staffGuard (S-25)
 * answer "is this screen for this persona" — each the twin of one API policy, and each the
 * condition its menu link is shown on (core/layout/navigation.ts).
 *
 * /pending and /admin/approvals are GONE (S-16, MP-03) along with the approval flow they served.
 * There is no waiting screen because there is nothing to wait for: registration produces an account
 * that works, and what decides whether somebody may train is the karnet.
 */
export const routes: Routes = [
  {
    path: 'login',
    title: 'Logowanie',
    data: { level: 'brand' } satisfies ScreenData,
    component: Login,
  },
  // GUARDED SINCE S-17, and it is the only anonymous route that is. Registration stopped being a
  // public door: /register is reachable only with an ?invitationCode= the club handed over, and
  // nothing in the app links to it. invitationGuard checks that a code is PRESENT, never that it is
  // valid — that answer belongs to the API, and asking for it first would be a code oracle.
  {
    path: 'register',
    title: 'Załóż konto',
    data: { level: 'brand' } satisfies ScreenData,
    component: Register,
    canActivate: [invitationGuard],
  },
  // The pre-login recovery flow (S-13). GUARD-FREE, like login — a visitor who cannot sign in is
  // exactly who these are for, and authGuard would bounce them to /login, which is the screen they
  // just failed at. Register used to be grouped here and no longer is; see above.
  //
  // LAZY: they sit in the anonymous bundle's blast radius otherwise, and most visitors never open
  // them. /reset-password reads its email and token from the QUERY STRING, never the path —
  // Identity's tokens contain characters a path segment mangles.
  {
    path: 'forgot-password',
    title: 'Nie pamiętasz hasła?',
    data: { level: 'brand' } satisfies ScreenData,
    loadComponent: () =>
      import('./features/auth/forgot-password/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reset-password',
    title: 'Ustaw nowe hasło',
    data: { level: 'brand' } satisfies ScreenData,
    loadComponent: () =>
      import('./features/auth/reset-password/reset-password').then((m) => m.ResetPassword),
  },
  // EAGER, as Home was before it. This is the screen every approved account lands on, and a lazy
  // chunk here would put a network round trip between signing in and seeing anything — the one place
  // in the app where that cost is paid by everyone, every visit. The 500 kB budget in angular.json is
  // what governs whether it stays that way.
  {
    path: '',
    title: 'Start',
    data: { level: 'brand' } satisfies ScreenData,
    component: Dashboard,
    canActivate: [authGuard, activeMemberGuard],
  },
  {
    path: 'admin/members',
    title: 'Członkowie',
    data: { level: 'tab' } satisfies ScreenData,
    component: Members,
    canActivate: [authGuard, adminGuard],
  },
  // 'new' BEFORE ':id', or the literal would be matched as an id and the form would try to load a
  // member called "new". Lazy, like every admin form: only an admin creating or editing a record
  // ever downloads it.
  {
    path: 'admin/members/new',
    title: 'Nowy członek',
    data: { level: 'child', parent: '/admin/members' } satisfies ScreenData,
    loadComponent: () => import('./features/admin/members/member-form').then((m) => m.MemberForm),
    canActivate: [authGuard, adminGuard],
  },
  // BEFORE ':id', or ':id' would swallow the whole two-segment path and the form would try to load
  // a member called "passes"... which it would not, but the route would never match. Same ordering
  // rule as 'new' above.
  {
    path: 'admin/members/:id/passes',
    title: 'Karnety',
    data: { level: 'child', parent: '/admin/members' } satisfies ScreenData,
    loadComponent: () =>
      import('./features/admin/members/member-passes').then((m) => m.MemberPasses),
    canActivate: [authGuard, adminGuard],
  },
  // A member's training plan, reached through the member (S-22, UX-07). BEFORE ':id' for the same
  // reason as ':id/passes'. The builder is mounted a second time under /trainer/members below; the
  // two mounts differ only in their guard and the list "back" returns to.
  //
  // LAZY, and it must stay so: the builder pulls in @angular/cdk's drag-drop, which has to land in
  // the builder's own chunk and nowhere else.
  {
    path: 'admin/members/:id/plan',
    title: 'Plan',
    data: { level: 'child', parent: '/admin/members' } satisfies ScreenData,
    loadComponent: () => import('./features/trainer/plans/plan-builder').then((m) => m.PlanBuilder),
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/members/:id',
    title: 'Edytuj członka',
    data: { level: 'child', parent: '/admin/members' } satisfies ScreenData,
    loadComponent: () => import('./features/admin/members/member-form').then((m) => m.MemberForm),
    canActivate: [authGuard, adminGuard],
  },
  // LAZY, both of them, and deliberately (S-07). They are the only routes that pull in
  // angular-calendar plus date-fns and its two drag/resize peers; eagerly loaded that lands in the
  // initial bundle, which sits at 513.15 kB (measured after S-20) against a 600 kB warning. It also
  // means login and register — everything reachable without a session — never download a calendar.
  //
  // A STAFF SCREEN since S-25: a member has no schedule, and the API refuses them one. A trainer sees
  // only the classes they instruct (narrowed on the server), an admin every class.
  {
    path: 'schedule',
    title: 'Grafik',
    data: { level: 'tab' } satisfies ScreenData,
    loadComponent: () => import('./features/schedule/schedule').then((m) => m.Schedule),
    canActivate: [authGuard, staffGuard],
  },
  // LAZY TOO, but for the opposite reason: /my-classes must not pull the calendar in, and loading it
  // eagerly beside routes that do is how it eventually would. It is a plain list by design (FR-010).
  // memberGuard (S-25): staff are never booked as participants, and /api/bookings/mine refuses them.
  {
    path: 'my-classes',
    title: 'Zajęcia',
    data: { level: 'tab' } satisfies ScreenData,
    loadComponent: () => import('./features/my-classes/my-classes').then((m) => m.MyClasses),
    canActivate: [authGuard, memberGuard],
  },
  {
    path: 'admin/classes',
    title: 'Grafik',
    data: { level: 'tab' } satisfies ScreenData,
    loadComponent: () => import('./features/admin/classes/classes').then((m) => m.Classes),
    canActivate: [authGuard, adminGuard],
  },
  // 'new' MUST precede ':id', or the literal segment is swallowed by the parameter.
  {
    path: 'admin/classes/new',
    title: 'Nowe zajęcia',
    data: { level: 'child', parent: '/admin/classes' } satisfies ScreenData,
    component: ClassForm,
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/classes/:id',
    title: 'Edytuj zajęcia',
    data: { level: 'child', parent: '/admin/classes' } satisfies ScreenData,
    component: ClassForm,
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/class-types',
    title: 'Typy zajęć',
    data: { level: 'tab' } satisfies ScreenData,
    component: ClassTypes,
    canActivate: [authGuard, adminGuard],
  },
  // 'new' MUST precede ':id' here too, or the literal segment is swallowed by the parameter.
  {
    path: 'admin/class-types/new',
    title: 'Nowy typ zajęć',
    data: { level: 'child', parent: '/admin/class-types' } satisfies ScreenData,
    component: ClassTypeForm,
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/class-types/:id',
    title: 'Edytuj typ zajęć',
    data: { level: 'child', parent: '/admin/class-types' } satisfies ScreenData,
    component: ClassTypeForm,
    canActivate: [authGuard, adminGuard],
  },
  // S-10's exercise library. LAZY, and not for the reason the two routes above are: these screens
  // pull in nothing heavy. Eagerly loaded they still cost ~28 kB, which took the initial bundle from
  // 475 kB to 502.88 kB - past the 500 kB budget in angular.json, so `npm run build` started warning.
  // Lazy chunks keep the budget green and cost nothing an admin will notice.
  {
    path: 'admin/exercises',
    title: 'Ćwiczenia',
    data: { level: 'tab' } satisfies ScreenData,
    loadComponent: () => import('./features/admin/exercises/exercises').then((m) => m.Exercises),
    canActivate: [authGuard, adminGuard],
  },
  // 'new' MUST precede ':id' here too, or the literal segment is swallowed by the parameter.
  {
    path: 'admin/exercises/new',
    title: 'Nowe ćwiczenie',
    data: { level: 'child', parent: '/admin/exercises' } satisfies ScreenData,
    loadComponent: () =>
      import('./features/admin/exercises/exercise-form').then((m) => m.ExerciseForm),
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/exercises/:id/edit',
    title: 'Edytuj ćwiczenie',
    data: { level: 'child', parent: '/admin/exercises' } satisfies ScreenData,
    loadComponent: () =>
      import('./features/admin/exercises/exercise-form').then((m) => m.ExerciseForm),
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/exercises/:id',
    title: 'Ćwiczenie',
    data: { level: 'child', parent: '/admin/exercises' } satisfies ScreenData,
    loadComponent: () =>
      import('./features/admin/exercises/exercise-detail').then((m) => m.ExerciseDetail),
    canActivate: [authGuard, adminGuard],
  },
  // The trainer's member list (S-22, UX-08), which replaced S-11's /trainer/plans: a plan is reached
  // through its member. LAZY for the exercise-library reason — the initial bundle sits close to its
  // budget. BEFORE ':id/plan', by the literal-before-parameter habit.
  {
    path: 'trainer/members',
    title: 'Członkowie',
    data: { level: 'tab' } satisfies ScreenData,
    loadComponent: () =>
      import('./features/trainer/members/trainer-members').then((m) => m.TrainerMembers),
    canActivate: [authGuard, trainerGuard],
  },
  // The trainer's mount of the plan builder (S-22, UX-08). trainerGuard admits admins too, but an
  // admin reaches the builder through /admin/members/:id/plan — the URL says who the screen is for.
  // LAZY, so @angular/cdk's drag-drop stays in the builder's own chunk and nowhere else.
  {
    path: 'trainer/members/:id/plan',
    title: 'Plan',
    data: { level: 'child', parent: '/trainer/members' } satisfies ScreenData,
    loadComponent: () => import('./features/trainer/plans/plan-builder').then((m) => m.PlanBuilder),
    canActivate: [authGuard, trainerGuard],
  },
  // The member's own plan. memberGuard since S-25, which reversed "every approved account has a plan
  // surface, the trainer's own included": trainers and admins hold no plan, and the API applies
  // MemberOnly at this group.
  {
    path: 'my-plan',
    title: 'Plan',
    data: { level: 'tab' } satisfies ScreenData,
    loadComponent: () => import('./features/my-plan/my-plan').then((m) => m.MyPlan),
    canActivate: [authGuard, memberGuard],
  },
  {
    path: 'my-plan/exercises/:id',
    title: 'Ćwiczenie',
    data: { level: 'child', parent: '/my-plan' } satisfies ScreenData,
    loadComponent: () =>
      import('./features/my-plan/plan-exercise-detail').then((m) => m.PlanExerciseDetail),
    canActivate: [authGuard, memberGuard],
  },
  // S-13's profile screen. authGuard ONLY, never activeMemberGuard — the API's /api/profile group
  // makes the same choice for the same reason: an account created before S-13 has no contact
  // details, and it must be able to supply them while still awaiting approval.
  //
  // LAZY like its neighbours: the initial bundle sits close to the 500 kB budget in angular.json,
  // and a screen most members open twice has no business in it.
  {
    path: 'profile',
    title: 'Moje konto',
    data: { level: 'child', parent: '/more' } satisfies ScreenData,
    loadComponent: () => import('./features/profile/profile').then((m) => m.Profile),
    canActivate: [authGuard],
  },
  // S-12's hub for everything the bottom bar does not give a tab: the account screen, logout, and —
  // since S-25 — the persona's own overflow from core/layout/navigation.ts. authGuard ONLY, deliberately — once the header's links are hidden on a phone this is
  // the only path a Pending member has to /profile, and activeMemberGuard would bounce them off it.
  //
  // LAZY: the initial bundle now carries the dashboard, and a screen reached by one tap out of five
  // has no business in it.
  {
    path: 'more',
    title: 'Więcej',
    data: { level: 'tab' } satisfies ScreenData,
    loadComponent: () => import('./features/more/more').then((m) => m.More),
    canActivate: [authGuard],
  },
  { path: '**', redirectTo: '' },
];
