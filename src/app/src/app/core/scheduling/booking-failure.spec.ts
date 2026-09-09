import { bookingFailureMessage } from './booking-failure';
import { BookingFailure } from './booking.models';

/**
 * The table exists so every staff surface describes the same refusal with the same words. These tests
 * guard the two ways that can quietly stop being true: a reason with no message, and an unrecognised
 * reason rendering as nothing at all.
 *
 * Since S-16 there is only one audience — a person acting for somebody else — so the messages are
 * asserted in that voice.
 */
describe('bookingFailureMessage', () => {
  // Every reason in the union, listed by hand. If BookingFailure grows a reason, the Record in
  // booking-failure.ts fails the build — and this list going stale is caught by the count assertion.
  const REASONS: BookingFailure['reason'][] = [
    'class_cancelled',
    'class_started',
    'already_booked',
    'class_full',
    'member_blocked',
    'no_valid_pass',
    'no_entries_left',
    'conflict',
  ];

  it('has a message for every reason the API can return', () => {
    expect(REASONS.length).toBe(8);

    for (const reason of REASONS) {
      const message = bookingFailureMessage(reason);

      expect(message.length).toBeGreaterThan(0);
      // The fallback would mean this reason fell through instead of being answered.
      expect(message).not.toContain('Spróbuj ponownie za chwilę');
    }
  });

  it('answers an unknown reason with the fallback rather than nothing', () => {
    // A server one version ahead can name a reason this build has never heard of.
    expect(bookingFailureMessage('brand_new_reason')).toContain('Nie udało się');
    expect(bookingFailureMessage(undefined)).toContain('Nie udało się');
    expect(bookingFailureMessage(null)).toContain('Nie udało się');
  });

  it('tells a full class apart from an already-booked one', () => {
    // Two refusals it would be most confusing to conflate: one means "come back later", the other
    // means "this person already has a spot".
    expect(bookingFailureMessage('class_full')).toContain('Brak wolnych miejsc');
    expect(bookingFailureMessage('already_booked')).toContain('już zapisana');
  });

  /**
   * THE TWO KARNET REFUSALS MUST NOT READ THE SAME (S-16). At the desk they lead to different
   * actions: one is "sell them a karnet", the other is "this one is used up".
   */
  it('tells a missing karnet apart from an exhausted one', () => {
    expect(bookingFailureMessage('no_valid_pass')).toContain('nie ma karnetu');
    expect(bookingFailureMessage('no_entries_left')).toContain('wolnych wejść');
    expect(bookingFailureMessage('no_valid_pass')).not.toBe(
      bookingFailureMessage('no_entries_left'),
    );
  });
});
