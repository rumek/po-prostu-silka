import { clubToday } from './club-today';

describe('clubToday', () => {
  /**
   * The point of the helper: at 23:30 UTC on 1 October it is already 2 October at the gym (CEST,
   * UTC+2), and a payment dated "today" must be the gym's day, which is what the server checks.
   */
  it('reads the club-local day, not the UTC one', () => {
    expect(clubToday(new Date('2026-10-01T23:30:00Z'))).toBe('2026-10-02');
  });

  it('formats the day as YYYY-MM-DD', () => {
    expect(clubToday(new Date('2026-01-05T12:00:00Z'))).toBe('2026-01-05');
  });
});
