import { fromLocalInputValue, toLocalInputValue } from './local-datetime';

/**
 * These conversions are the silent failure mode of this slice: get them backwards and every saved
 * class shifts by the local UTC offset with nothing throwing. The round-trip assertions are what
 * make that loud.
 */
describe('local-datetime', () => {
  it('renders a UTC instant as the local wall clock the input expects', () => {
    const isoUtc = '2026-09-04T20:00:00+00:00';
    const local = new Date(isoUtc);

    const pad = (n: number) => String(n).padStart(2, '0');
    const expected =
      `${local.getFullYear()}-${pad(local.getMonth() + 1)}-${pad(local.getDate())}` +
      `T${pad(local.getHours())}:${pad(local.getMinutes())}`;

    expect(toLocalInputValue(isoUtc)).toBe(expected);
  });

  it('produces a value the datetime-local input can accept', () => {
    // Exactly YYYY-MM-DDTHH:mm — a stray seconds component or a trailing Z makes the input blank.
    expect(toLocalInputValue('2026-09-04T20:00:00+00:00')).toMatch(
      /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/,
    );
  });

  it('round-trips UTC to local and back without drift', () => {
    for (const isoUtc of [
      '2026-01-15T09:30:00+00:00', // winter
      '2026-07-15T09:30:00+00:00', // summer — different offset in most zones
      '2026-09-04T20:00:00+00:00',
      '2026-10-30T21:00:00+00:00', // the far side of Europe's DST change
    ]) {
      const back = fromLocalInputValue(toLocalInputValue(isoUtc));

      // Minute precision: the input carries no seconds, so that is the resolution being preserved.
      expect(new Date(back).getTime()).toBe(
        Math.floor(new Date(isoUtc).getTime() / 60_000) * 60_000,
      );
    }
  });

  it('reads the input value as local time, not as UTC', () => {
    const parsed = new Date(fromLocalInputValue('2026-09-04T22:00'));

    // Whatever the runner's zone, 22:00 typed by the admin must come back as 22:00 locally.
    expect(parsed.getHours()).toBe(22);
    expect(parsed.getMinutes()).toBe(0);
  });
});
