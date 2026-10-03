import { Persona } from '../auth/persona';
import { IconName } from '../../shared/icons/icon';

/** One menu destination. `exact` matters for exactly one of them — Start, see below. */
export interface NavLink {
  readonly route: string;
  readonly label: string;
  /** Rendered by the bottom bar only; the header and /more print the label alone. */
  readonly icon: IconName;
  readonly exact: boolean;
}

/** What each of the three menus shows for one persona. */
export interface Navigation {
  /** The header, above 30rem. */
  readonly header: readonly NavLink[];
  /** The phone's bottom bar, at 30rem and below. Never more than five. */
  readonly bar: readonly NavLink[];
  /** The persona's own entries on /more, above the account link and logout every account gets. */
  readonly more: readonly NavLink[];
}

// `/` MUST be exact: every other route has it as a prefix, so without it Start reads as active on
// every screen in the app.
const START: NavLink = { route: '/', label: 'Start', icon: 'home', exact: true };
const MORE: NavLink = { route: '/more', label: 'Więcej', icon: 'more', exact: false };

const MY_CLASSES: NavLink = {
  route: '/my-classes',
  label: 'Zajęcia',
  icon: 'booking',
  exact: false,
};
const MY_PLAN: NavLink = { route: '/my-plan', label: 'Plan', icon: 'plan', exact: false };

const SCHEDULE: NavLink = { route: '/schedule', label: 'Grafik', icon: 'calendar', exact: false };
const TRAINER_MEMBERS: NavLink = {
  route: '/trainer/members',
  label: 'Członkowie',
  icon: 'members',
  exact: false,
};

// S-37. The trainer's groups: the ones whose upcoming classes they instruct, each opening its roster.
const TRAINER_GROUPS: NavLink = {
  route: '/trainer/groups',
  label: 'Grupy',
  icon: 'group',
  exact: false,
};

// S-36. Both staff personas: any trainer arranges any member's makeup, so the list is the club's.
const MAKEUPS: NavLink = { route: '/makeups', label: 'Odrabianie', icon: 'repeat', exact: false };

const ADMIN_CLASSES: NavLink = { ...SCHEDULE, route: '/admin/classes' };
const ADMIN_MEMBERS: NavLink = { ...TRAINER_MEMBERS, route: '/admin/members' };
const CLASS_GROUPS: NavLink = {
  route: '/admin/class-groups',
  label: 'Grupy',
  icon: 'group',
  exact: false,
};
const EXERCISES: NavLink = {
  route: '/admin/exercises',
  label: 'Ćwiczenia',
  icon: 'hantle',
  exact: false,
};

// No persona (a blocked account): nothing but the way to the account screen and logout. The bar
// keeps Więcej, because on a phone the header is hidden and /more is the only path to either.
const NONE: Navigation = { header: [], bar: [MORE], more: [] };

/**
 * The one link table the header, the bottom bar and /more all read (S-25), so the three cannot
 * drift — and each link's persona is its route's guard by construction:
 *
 * - member — `/my-classes` and `/my-plan` are behind memberGuard;
 * - trainer — `/schedule` and `/makeups` behind staffGuard, `/trainer/members` and `/trainer/groups`
 *   behind trainerGuard;
 * - admin — `/schedule` or `/admin/classes`, and the admin lists, behind staffGuard / adminGuard.
 *
 * Moje konto and logout are NOT in the table: the shell and /more render them for every
 * authenticated account, a blocked one included, which is why a null persona gets empty header and
 * /more lists and a bar holding only Więcej — its phone path to those two.
 *
 * THE ADMIN'S "Grafik" DEPENDS ON THE VIEWPORT. At desk width it is the management calendar, which
 * refuses to render below it (UX-01); below, it is the schedule — the same classes, with the same
 * bookings overlay, on a screen that works on a phone. `desk` comes from the caller's
 * `mediaQuerySignal(DESK_MEDIA_QUERY, true)` so this stays a pure function.
 *
 * The admin needs /more for a destination (Grupy and Odrabianie — the sixth and seventh links do not
 * fit a five-slot bar), and so, since S-37, does the trainer, whose bar is full since S-36: their
 * Grupy lives there. The member still gets the Więcej tab: it is where the phone reaches Moje konto
 * and logout.
 */
export function navigationFor(persona: Persona | null, desk: boolean): Navigation {
  switch (persona) {
    case 'member':
      return {
        header: [START, MY_CLASSES, MY_PLAN],
        bar: [START, MY_CLASSES, MY_PLAN, MORE],
        more: [],
      };
    case 'trainer':
      return {
        header: [START, SCHEDULE, TRAINER_MEMBERS, MAKEUPS, TRAINER_GROUPS],
        bar: [START, SCHEDULE, TRAINER_MEMBERS, MAKEUPS, MORE],
        more: [TRAINER_GROUPS],
      };
    case 'admin': {
      const grafik = desk ? ADMIN_CLASSES : SCHEDULE;
      return {
        header: [START, grafik, ADMIN_MEMBERS, CLASS_GROUPS, EXERCISES, MAKEUPS],
        bar: [START, grafik, ADMIN_MEMBERS, EXERCISES, MORE],
        more: [CLASS_GROUPS, MAKEUPS],
      };
    }
    default:
      return NONE;
  }
}
