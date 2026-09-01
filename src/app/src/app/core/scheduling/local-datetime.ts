/**
 * Conversions between the API's ISO-8601 UTC instants and the value an `<input type="datetime-local">`
 * expects.
 *
 * This exists as its own module, rather than inline in the form, because it is the one place in this
 * slice where a mistake is SILENT. `datetime-local` carries no timezone: its value is a bare
 * wall-clock reading. Feed it a UTC string and the browser shows the wrong time; send its value back
 * as if it were UTC and every saved class shifts by the local offset. Nothing throws either way — the
 * class simply moves, which is exactly the class of bug the DST work in Phase 2 was about.
 */

const pad = (value: number): string => String(value).padStart(2, '0');

/**
 * ISO UTC instant → the `YYYY-MM-DDTHH:mm` local wall-clock string the input renders.
 *
 * Uses the local getters (getFullYear, getMonth, …) deliberately: their UTC counterparts would
 * produce a correct-looking string for the wrong clock.
 */
export function toLocalInputValue(isoUtc: string): string {
  const instant = new Date(isoUtc);

  return (
    `${instant.getFullYear()}-${pad(instant.getMonth() + 1)}-${pad(instant.getDate())}` +
    `T${pad(instant.getHours())}:${pad(instant.getMinutes())}`
  );
}

/**
 * The input's local wall-clock string → an ISO UTC instant for the API.
 *
 * A date-time string with no offset is parsed as LOCAL time, which is what makes this the correct
 * inverse of toLocalInputValue — the browser applies whatever offset was in force on that date,
 * including across a DST boundary.
 */
export function fromLocalInputValue(localValue: string): string {
  return new Date(localValue).toISOString();
}
