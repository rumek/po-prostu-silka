/**
 * The client-side halves of rules the server owns.
 *
 * <p>
 * ONE COPY, ON PURPOSE. These lived in both the register and the profile component, kept in step by
 * a comment — the exact drift `src/Application/Members/ContactDetails.cs` exists to prevent on the
 * server, reintroduced on the client. The two forms write the same five columns through the same
 * server-side helper, so a rule that differs between them is a value the member can save on one
 * screen and not the other.
 * </p>
 *
 * <p>
 * THE SERVER STAYS THE AUTHORITY. Nothing here is a security control; every rule is re-checked by
 * `ContactDetails.TryCreate`, and these only spare the member a round trip. Where they diverge, they
 * are deliberately the STRICTER side — a value this rejects and the server would have accepted costs
 * an unnecessary correction, whereas the reverse would cost a confusing server error on a field the
 * form said was fine.
 * </p>
 */

/** Matches Identity's RequiredLength in src/Program.cs. Keep the two in step. */
export const MIN_PASSWORD_LENGTH = 8;

/** Mirrors ContactDetails.PostalCodePattern exactly. */
export const POSTAL_CODE_PATTERN = /^\d{2}-\d{3}$/;

/**
 * Nine digits after separators, with an optional +48; the server normalises to the bare nine.
 *
 * <p>
 * Deliberately narrower than the server, which also strips '(' and ')', so `(12) 345 67 89` is
 * refused here and would have been accepted by the API. That is the safe direction (see above), not
 * an oversight — widen it here if members start typing brackets.
 * </p>
 */
export const PHONE_PATTERN = /^(?:\+?48[\s-]?)?(?:\d[\s-]?){8}\d$/;
