import { MemberAdminService } from '../admin/member-admin.service';
import { TrainingPlanService } from '../training/training-plan.service';

/** How many matches the bookings overlay's picker offers. Past this, it asks for a narrower phrase. */
export const CANDIDATE_RESULTS = 20;

/** Somebody the bookings overlay's picker may offer — the fields both member lists carry. */
export interface BookingCandidate {
  id: string;
  displayName: string;
  /** False for a person the club recorded who never registered — marked "— bez konta". */
  hasAccount: boolean;
}

/** One page of matches, with the total so the picker can say it is showing only some. */
export interface BookingCandidatePage {
  items: BookingCandidate[];
  total: number;
}

/**
 * Where the bookings overlay finds people to sign up (S-25). The overlay serves two personas: the
 * admin searches the admin member list (name or e-mail), and a trainer the trainer member list,
 * which is name-only and never carries an e-mail — and, since S-25, excludes staff, so it is
 * exactly the set a trainer may book. The placeholder travels with the search because what the box
 * accepts is a property of the source.
 */
export interface BookingCandidateSearch {
  readonly placeholder: string;
  find(phrase: string): Promise<BookingCandidatePage>;
}

/**
 * The admin's source: active members matching the phrase. It may include staff, which the API then
 * refuses with `member_is_staff` — the admin list is a list of accounts, and filtering it here would
 * hide a person the admin is looking for without saying why.
 */
export function adminCandidateSearch(members: MemberAdminService): BookingCandidateSearch {
  return {
    placeholder: 'Imię, nazwisko lub e-mail',
    async find(phrase) {
      const page = await members.getMembers({
        filter: 'Active',
        search: phrase,
        pageSize: CANDIDATE_RESULTS,
      });

      return {
        items: page.items.map((m) => ({
          id: m.id,
          displayName: m.displayName,
          hasAccount: m.userId !== null,
        })),
        total: page.total,
      };
    },
  };
}

/** The trainer's source: `/api/trainer/members`, name-only, members only. */
export function trainerCandidateSearch(plans: TrainingPlanService): BookingCandidateSearch {
  return {
    placeholder: 'Imię i nazwisko',
    async find(phrase) {
      const page = await plans.getTrainerMembers({ search: phrase, pageSize: CANDIDATE_RESULTS });

      return {
        items: page.items.map((m) => ({
          id: m.id,
          displayName: m.displayName,
          hasAccount: m.hasAccount,
        })),
        total: page.total,
      };
    },
  };
}
