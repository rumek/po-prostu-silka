import { bookingFailureMessage } from './booking-failure';
import { BookingFailure } from './booking.models';

/**
 * The table exists so the overlay, "Moje zajęcia" and the admin's panel describe the same refusal
 * with the same words. These tests guard the two ways that can quietly stop being true: a reason
 * with no message, and an unrecognised reason rendering as nothing at all.
 */
describe('bookingFailureMessage', () => {
  // Every reason in the union, listed by hand. If BookingFailure grows a reason, the Record in
  // booking-failure.ts fails the build — and this list going stale is caught by the count assertion.
  const REASONS: BookingFailure['reason'][] = [
    'class_cancelled',
    'class_started',
    'already_booked',
    'class_full',
    'not_booked',
    'conflict',
  ];

  it('has a message for every reason the API can return', () => {
    expect(REASONS.length).toBe(6);

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
    // The two refusals a member is most likely to hit, and the two it would be most confusing to
    // conflate: one means "come back later", the other means "you already have this".
    expect(bookingFailureMessage('class_full')).toContain('Brak wolnych miejsc');
    expect(bookingFailureMessage('already_booked')).toContain('już zapisany');
  });
});
