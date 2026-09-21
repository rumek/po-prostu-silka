import { accessCodeFailureMessage } from '../admin/access-code-failure';
import { blockFailureMessage } from '../admin/block-failure';
import { memberFailureMessage } from '../admin/member-failure';
import { memberListFailureMessage } from '../admin/member-list-failure';
import { membershipPassFailureMessage } from '../admin/membership-pass-failure';
import { trainerRoleFailureMessage } from '../admin/trainer-role-failure';
import { unblockFailureMessage } from '../admin/unblock-failure';
import {
  ACCESS_CODE_FAILURE_REASONS,
  BLOCK_FAILURE_REASONS,
  MEMBER_FAILURE_REASONS,
  MEMBER_LIST_FAILURE_REASONS,
  MEMBERSHIP_PASS_FAILURE_REASONS,
  TRAINER_ROLE_FAILURE_REASONS,
  UNBLOCK_FAILURE_REASONS,
} from '../admin/member-admin.models';
import { changePasswordFailureMessage } from '../auth/change-password-failure';
import { contactFailureMessage } from '../auth/contact-failure';
import { loginFailureMessage } from '../auth/login-failure';
import { registerFailureMessage } from '../auth/register-failure';
import { resetPasswordFailureMessage } from '../auth/reset-password-failure';
import {
  CHANGE_PASSWORD_FAILURE_REASONS,
  CONTACT_FAILURE_REASONS,
  LOGIN_FAILURE_REASONS,
  REGISTER_FAILURE_REASONS,
  RESET_PASSWORD_FAILURE_REASONS,
} from '../auth/auth.models';
import { bookingFailureMessage } from '../scheduling/booking-failure';
import { BOOKING_FAILURE_REASONS } from '../scheduling/booking.models';
import { classFailureMessage } from '../scheduling/class-failure';
import { classTypeFailureMessage } from '../scheduling/class-type-failure';
import { CLASS_TYPE_FAILURE_REASONS } from '../scheduling/class-type.models';
import { CLASS_FAILURE_REASONS, SCHEDULE_READ_FAILURE_REASONS } from '../scheduling/class.models';
import { scheduleReadFailureMessage } from '../scheduling/schedule-read-failure';
import { exerciseFailureMessage } from '../training/exercise-failure';
import { EXERCISE_FAILURE_REASONS } from '../training/exercise.models';
import { trainingPlanFailureMessage } from '../training/training-plan-failure';
import { TRAINING_PLAN_FAILURE_REASONS } from '../training/training-plan.models';

/**
 * THE ENFORCEMENT HALF OF S-19's DECISION.
 *
 * The `Record<Reason, string>` inside each table already makes a MISSING ENTRY a build error. What
 * types cannot catch is a union that never got a table at all — a new `*Failure` in a models file,
 * handled by a `switch` with a `default:` inside whichever component happened to call the endpoint,
 * which is exactly the state S-19 found: seventeen unions, three tables, nine display mechanisms.
 *
 * So the registry below is the contract. Adding a union to `core/` without adding it here leaves
 * this spec passing, which is why the count is asserted explicitly and the number is stated in
 * AGENTS.md: the failing assertion is what sends the next contributor to the rule.
 */
interface UnionUnderContract {
  name: string;
  reasons: readonly string[];
  message: (reason: unknown) => string;
  /** Reasons the table deliberately answers with the same sentence, as documented in its comments. */
  deliberateDuplicates?: readonly (readonly string[])[];
}

const UNIONS: readonly UnionUnderContract[] = [
  {
    name: 'BookingFailure',
    reasons: BOOKING_FAILURE_REASONS,
    message: bookingFailureMessage,
  },
  {
    name: 'ClassFailure',
    reasons: CLASS_FAILURE_REASONS,
    message: classFailureMessage,
    // class-failure.ts:38-44 states both groups outright: all three class-type refusals mean "this
    // type cannot be used", and both instructor refusals mean "pick someone else".
    deliberateDuplicates: [
      ['unknown_class_type', 'inactive_class_type', 'class_type_immutable'],
      ['unknown_instructor', 'instructor_not_trainer'],
    ],
  },
  {
    name: 'ScheduleReadFailure',
    reasons: SCHEDULE_READ_FAILURE_REASONS,
    message: scheduleReadFailureMessage,
  },
  {
    name: 'ClassTypeFailure',
    reasons: CLASS_TYPE_FAILURE_REASONS,
    message: classTypeFailureMessage,
  },
  {
    name: 'ExerciseFailure',
    reasons: EXERCISE_FAILURE_REASONS,
    message: exerciseFailureMessage,
  },
  {
    name: 'TrainingPlanFailure',
    reasons: TRAINING_PLAN_FAILURE_REASONS,
    message: trainingPlanFailureMessage,
    // Both pairs are documented in training-plan-failure.ts: the trainer's next move is identical
    // whichever half of each pair the server meant.
    deliberateDuplicates: [
      ['unknown_exercise', 'inactive_exercise'],
      ['member_not_found', 'member_not_active'],
    ],
  },
  {
    name: 'LoginFailure',
    reasons: LOGIN_FAILURE_REASONS,
    message: loginFailureMessage,
  },
  {
    name: 'RegisterFailure',
    reasons: REGISTER_FAILURE_REASONS,
    message: registerFailureMessage,
    // register-failure.ts: telling an invalid code apart from an unknown one would confirm to a
    // stranger that a code once existed, which the server already refuses to do.
    deliberateDuplicates: [['invalid_member_code', 'unknown_member_code']],
  },
  {
    name: 'ContactFailureReason (Profile)',
    reasons: CONTACT_FAILURE_REASONS,
    message: contactFailureMessage,
  },
  {
    name: 'ChangePasswordFailure',
    reasons: CHANGE_PASSWORD_FAILURE_REASONS,
    message: changePasswordFailureMessage,
  },
  {
    name: 'ResetPasswordFailure',
    reasons: RESET_PASSWORD_FAILURE_REASONS,
    message: resetPasswordFailureMessage,
  },
  {
    name: 'MemberFailure',
    reasons: MEMBER_FAILURE_REASONS,
    message: memberFailureMessage,
  },
  {
    name: 'TrainerRoleFailure',
    reasons: TRAINER_ROLE_FAILURE_REASONS,
    message: trainerRoleFailureMessage,
  },
  {
    name: 'BlockFailure',
    reasons: BLOCK_FAILURE_REASONS,
    message: blockFailureMessage,
  },
  {
    name: 'UnblockFailure',
    reasons: UNBLOCK_FAILURE_REASONS,
    message: unblockFailureMessage,
  },
  {
    name: 'AccessCodeFailure',
    reasons: ACCESS_CODE_FAILURE_REASONS,
    message: accessCodeFailureMessage,
  },
  {
    name: 'MembershipPassFailure',
    reasons: MEMBERSHIP_PASS_FAILURE_REASONS,
    message: membershipPassFailureMessage,
  },
  {
    name: 'MemberListFailure',
    reasons: MEMBER_LIST_FAILURE_REASONS,
    message: memberListFailureMessage,
  },
];

describe('the failure-message contract', () => {
  /**
   * The count is asserted, not derived. A union added to `core/` and left out of the registry above
   * would otherwise slip through silently — which is the exact failure mode this whole spec exists
   * to stop, one level up.
   */
  it('covers all eighteen failure unions', () => {
    expect(UNIONS).toHaveLength(18);
    expect(new Set(UNIONS.map((union) => union.name)).size).toBe(18);
  });

  describe.each(UNIONS.map((union) => [union.name, union] as const))('%s', (_name, union) => {
    it('has at least one reason', () => {
      expect(union.reasons.length).toBeGreaterThan(0);
    });

    it('answers every reason with a real sentence, not the fallback', () => {
      const fallback = union.message('a_reason_no_build_will_ever_know');

      for (const reason of union.reasons) {
        const message = union.message(reason);

        expect(message.length, `${reason} has no message`).toBeGreaterThan(0);
        expect(message, `${reason} fell through to the fallback`).not.toBe(fallback);
      }
    });

    it('answers an unrecognised reason with the fallback rather than nothing', () => {
      // A server one version ahead can name a reason this build has never heard of.
      for (const reason of ['brand_new_reason', undefined, null, 42, {}]) {
        const message = union.message(reason);

        expect(typeof message).toBe('string');
        expect(message.length).toBeGreaterThan(0);
      }
    });

    /**
     * `Object.hasOwn`, not `in` — with `in` these resolve up the prototype chain to a FUNCTION typed
     * as string and render as source text in front of a user. Asserted per union, because the guard
     * is now written once and every table depends on that one copy being right.
     */
    it.each(['constructor', 'toString', '__proto__'])(
      'answers the inherited property %s with the fallback',
      (reason) => {
        expect(union.message(reason)).toBe(union.message('a_reason_no_build_will_ever_know'));
      },
    );

    /**
     * TWO REFUSALS THAT READ THE SAME ARE ONE REFUSAL. Where a table means that — three class-type
     * refusals that all mean "you cannot use this type" — it says so in its own comments, and the
     * registry entry repeats the claim. Anything else sharing a sentence is drift.
     */
    it('gives each reason its own sentence, except where the table says otherwise', () => {
      const allowed = new Set<string>();
      for (const group of union.deliberateDuplicates ?? []) {
        for (const reason of group) {
          allowed.add(reason);
        }
      }

      const seen = new Map<string, string>();

      for (const reason of union.reasons) {
        if (allowed.has(reason)) {
          continue;
        }

        const message = union.message(reason);
        const previous = seen.get(message);

        expect(
          previous,
          `${reason} and ${previous} share a sentence without the table saying they should`,
        ).toBeUndefined();

        seen.set(message, reason);
      }
    });

    it('declares every documented duplicate group as an actual duplicate', () => {
      // Keeps the registry honest in the other direction: a group listed here that has since been
      // given distinct wording is a stale exemption hiding future drift.
      for (const group of union.deliberateDuplicates ?? []) {
        const messages = new Set(group.map((reason) => union.message(reason)));

        expect(messages.size, `${group.join(', ')} no longer share a sentence`).toBe(1);
      }
    });
  });
});
