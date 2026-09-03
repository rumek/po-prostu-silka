namespace po_prostu_silka.Application.Persistence;

/// <summary>
/// What a commit did, for callers that have to tell the two failure modes apart.
///
/// <see cref="IUnitOfWork.TrySaveChangesAsync"/> collapses both into <c>false</c>, which was enough
/// while the only guarded write was a status flip. A booking is guarded by BOTH a concurrency token
/// and a filtered unique index, and the two mean different things to the caller, so S-08 needed a
/// three-valued answer rather than a boolean.
/// </summary>
public enum SaveOutcome
{
    /// <summary>Committed.</summary>
    Saved = 0,

    /// <summary>
    /// A concurrency token no longer matched: another request committed first and nothing of ours was
    /// written. Re-read and try again.
    /// </summary>
    ConcurrencyConflict = 1,

    /// <summary>
    /// A unique index rejected the write. Nothing of ours was written.
    ///
    /// <para>
    /// This value exists because it was the recurring hole in this codebase: three separate
    /// implementation reviews found a pre-check followed by a write where the losing race surfaced as
    /// an unhandled DbUpdateException - a 500 where the caller should have seen a clean 409 - and each
    /// time it was accepted as risk because catching it needed EF Core types in Application. It does
    /// not any more; the mapping happens in Infrastructure and only this enum crosses the boundary.
    /// </para>
    /// </summary>
    UniqueViolation = 2,
}

/// <summary>
/// Commits whatever the current request has changed, as one transaction.
///
/// Exists because a handler sometimes has to make a domain change and enqueue an outbox message in
/// the SAME unit of work — S-01's approve is the first such case: the status flip and the approval
/// email must land together or not at all. <see cref="Notifications.IOutboxEnqueuer"/> deliberately
/// does not save, so somebody has to, and Application cannot reference EF Core (AGENTS.md layering).
///
/// IMPORTANT: this is one SaveChangesAsync, which is atomic on its own and covered by the retry
/// strategy. It does NOT open an explicit transaction. If a caller ever needs several saves to be
/// atomic, it must go through Database.CreateExecutionStrategy().ExecuteAsync(...) in Infrastructure,
/// because EnableRetryOnFailure is on (Program.cs) and BeginTransaction otherwise throws at runtime.
/// S-08 deliberately avoided needing that: rotating a concurrency token makes a read-check-write
/// atomic within ONE SaveChangesAsync, so the booking path never opens a transaction of its own.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits, but reports a lost optimistic-concurrency race as <c>false</c> instead of throwing.
    ///
    /// Exists so a handler can make a read-check-write sequence atomic without referencing EF Core:
    /// the caller rotates a concurrency token, and a <c>false</c> here means another request got
    /// there first and nothing was committed. S-01's approve uses it so two admins clicking the same
    /// row cannot both enqueue the approval email.
    ///
    /// Only concurrency conflicts are absorbed. Every other failure still throws — including a unique
    /// index rejection, which is a DbUpdateException rather than a DbUpdateConcurrencyException and
    /// would escape as a 500. A caller guarded by a unique index wants
    /// <see cref="TrySaveAsync"/> instead.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits and reports which of the two guarded failure modes occurred, without throwing for
    /// either. Everything else still throws.
    ///
    /// Use this rather than <see cref="TrySaveChangesAsync"/> when the write is protected by a unique
    /// index as well as a concurrency token, which is the shape every booking write has.
    /// </summary>
    Task<SaveOutcome> TrySaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Throws away everything the current request has changed but not committed, so the caller can
    /// start its attempt over against fresh state.
    ///
    /// Exists for the booking retry loop. After a failed commit the tracked graph still holds the
    /// rejected insert and an entity whose concurrency token is stale, so a second attempt would
    /// re-send the same doomed write; re-reading without discarding returns the tracked (stale)
    /// instance rather than the database's. Nothing else in the application needs this — reach for it
    /// only inside a retry.
    /// </summary>
    void DiscardChanges();
}
