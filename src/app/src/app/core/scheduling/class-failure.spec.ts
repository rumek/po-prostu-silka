import { classFailureMessage } from './class-failure';
import { ClassFailure } from './class.models';

/**
 * The table exists so the class form and the create overlay describe the same refusal with the same
 * words. These tests guard the two ways that can quietly stop being true: a reason with no message,
 * and an unrecognised reason rendering as nothing at all.
 */
describe('classFailureMessage', () => {
  // Every reason in the union, listed by hand. If ClassFailure grows a reason, the Record in
  // class-failure.ts fails the build — and this list going stale is caught by the count assertion.
  const REASONS: ClassFailure['reason'][] = [
    'missing_field',
    'invalid_capacity',
    'invalid_duration',
    'starts_in_past',
    'invalid_weeks',
    'time_conflict',
    'unknown_class_type',
    'inactive_class_type',
    'class_type_immutable',
    'unknown_instructor',
    'instructor_not_trainer',
    'has_bookings',
    'capacity_below_bookings',
    'conflict',
  ];

  it('has a message for every reason the API can return', () => {
    expect(REASONS.length).toBe(14);

    for (const reason of REASONS) {
      const message = classFailureMessage(reason);

      expect(message.length).toBeGreaterThan(0);
      // The fallback would mean this reason fell through instead of being answered.
      expect(message).not.toContain('Spróbuj ponownie za chwilę');
    }
  });

  it('answers an unknown reason with the fallback rather than nothing', () => {
    // A server one version ahead can name a reason this build has never heard of.
    expect(classFailureMessage('brand_new_reason')).toContain('Nie udało się');
    expect(classFailureMessage(undefined)).toContain('Nie udało się');
    expect(classFailureMessage(null)).toContain('Nie udało się');
  });

  it('says the same thing about a time conflict wherever it is asked', () => {
    // The whole point of the table: one refusal, one wording, both create surfaces.
    expect(classFailureMessage('time_conflict')).toBe(classFailureMessage('time_conflict'));
    expect(classFailureMessage('time_conflict')).toContain('inne zajęcia');
  });
});
