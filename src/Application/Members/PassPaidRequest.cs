namespace po_prostu_silka.Application.Members;

/// <summary>
/// What a staff member submits to mark a karnet paid or unpaid (pass-paid-flag).
///
/// <para>
/// AN EXPLICIT VALUE, NEVER "TOGGLE". A date marks it paid on that club-local day; <c>null</c> marks it
/// unpaid. Two staff acting on the same karnet at once therefore converge on what the later one meant,
/// instead of flipping each other's write back.
/// </para>
/// </summary>
public record PassPaidRequest(DateOnly? PaidAt);
