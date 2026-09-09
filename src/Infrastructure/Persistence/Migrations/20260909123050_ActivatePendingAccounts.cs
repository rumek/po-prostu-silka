using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Flips every account still awaiting approval to Active (S-16, MP-03).
    ///
    /// <para>
    /// WHY THIS MUST LAND BEFORE THE CODE THAT STOPS PRODUCING <c>Pending</c>. Approval is the only
    /// transition out of that status. The moment the approve route is gone, an account left at
    /// <c>Pending</c> can still log in and then fails every policy in the app — a login that works
    /// and an account that can do nothing, with nobody able to fix it. Activating them first closes
    /// that window entirely.
    /// </para>
    ///
    /// <para>
    /// THE DEPLOY INVARIANT THIS RELIES ON is the one <c>AddMembers</c> records: migrations are
    /// applied before the new artifact starts, so the schema is always at or ahead of the code and
    /// never behind. The previous artifact tolerates Active accounts perfectly well — it is the state
    /// approval already produced — so there is no window in which this data is wrong for whichever
    /// build is serving.
    /// </para>
    ///
    /// <para>
    /// The SECOND data migration in this project's history, and it follows the first one's shape:
    /// <c>migrationBuilder.Sql</c> with a raw string, written so that applying it twice is a no-op.
    /// </para>
    /// </summary>
    public partial class ActivatePendingAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IDEMPOTENT BY CONSTRUCTION: the WHERE clause is what it changes, so a second run
            // matches nothing. Same technique AddMembers' INSERT ... WHERE NOT EXISTS uses, and it
            // matters for the same reason - a migration may be re-applied against a database whose
            // history table was rebuilt.
            //
            // The literals name AccountStatus.Pending (0) and AccountStatus.Active (1). The enum pins
            // its numeric values for exactly this kind of dependency; see Domain/AccountStatus.cs.
            //
            // MEMBERSHIP IS NOT TOUCHED. A Pending account already carries an ACTIVE membership -
            // approval gates the login, not the person - so there is nothing to flip on Members, and
            // touching it would silently unblock anyone the club had blocked.
            migrationBuilder.Sql("""
                UPDATE [AspNetUsers] SET [Status] = 1 WHERE [Status] = 0;
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// A DELIBERATE NO-OP, and the only irreversible step in this stream.
        ///
        /// <para>
        /// Which accounts were pending before this ran is not recorded anywhere - the flip is the
        /// only evidence it happened, and it is indistinguishable from an ordinary approval. There is
        /// therefore nothing to restore. The alternative, re-pending accounts by some heuristic,
        /// would lock real members out of an app they have been using, which is far worse than a
        /// rollback that leaves them able to log in.
        /// </para>
        ///
        /// <para>
        /// Rolling back the CODE is unaffected: the previous artifact reads Active accounts as
        /// approved and normal. That is what makes leaving this empty safe rather than merely
        /// convenient.
        /// </para>
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
