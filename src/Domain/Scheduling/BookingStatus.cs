namespace po_prostu_silka.Domain.Scheduling;

/// <summary>
/// A booking's lifecycle. Cancelling is a visible STATE, not a delete: prd.md FR-009 requires the
/// cancelled booking to stay in history, so the row survives and only its status moves.
///
/// The numeric values are explicit and must not be reordered — the column is persisted as an int, so
/// renumbering would silently reinterpret every existing row. <see cref="Active"/> is 0 so it is the
/// default for any row inserted without an explicit value, and so the filtered unique index
/// <c>IX_Bookings_Class_Member_Active</c> can name it as a literal in its filter expression.
/// </summary>
public enum BookingStatus
{
    /// <summary>The member holds this spot. The only state that counts against capacity.</summary>
    Active = 0,

    /// <summary>
    /// Released — by the member, by an admin, or by the cascade that runs when the member is blocked.
    /// Keeps its place in history and frees the spot; the member may book the class again, which is
    /// why the uniqueness index is filtered to <see cref="Active"/> alone.
    /// </summary>
    Cancelled = 1,
}
