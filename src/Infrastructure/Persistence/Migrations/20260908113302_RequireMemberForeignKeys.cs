using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// S-14 Phase 8: the member keys become REQUIRED, and the two filtered unique indexes over them
    /// go back to the plain status filter.
    ///
    /// <para>
    /// SAFE TO RUN BEFORE THE ARTIFACT SHIPS, which is what deploy.yml requires ("schema is always
    /// &gt;= code"). Every row already carries these keys: the Phase 4 migration backfilled them and
    /// every write path has populated them since. The N-1 artifact still writes them too, so a
    /// rollback across this migration keeps inserting rows that satisfy the NOT NULL — the reason
    /// the write side had to move a release before the schema did.
    /// </para>
    ///
    /// <para>
    /// The legacy account columns were made nullable back in Phase 6, ahead of the plan, because
    /// booking an accountless member into a class was impossible while they were NOT NULL. Nothing
    /// left to do here for them; they are dropped one release later.
    /// </para>
    /// </summary>
    public partial class RequireMemberForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // THE BACKFILL RUNS AGAIN, and it is not superfluous. Phase 4 filled every row that
            // existed then, and every artifact since has written both keys — but a rollback to the
            // Phase 3 artifact at any point in between would have inserted rows carrying only the
            // account key. Re-running the same idempotent UPDATE..FROM costs one scan of a small
            // table and turns "the ALTER fails on production at 2am" into "there was nothing to do".
            //
            // A row with NEITHER key cannot exist: the account column was NOT NULL until Phase 6, and
            // every artifact that could write a NULL into it already writes the member key.
            migrationBuilder.Sql("""
                UPDATE b
                SET b.[MemberId] = m.[Id]
                FROM [Bookings] AS b
                INNER JOIN [Members] AS m ON m.[UserId] = b.[MemberUserId]
                WHERE b.[MemberId] IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE p
                SET p.[MemberId] = m.[Id]
                FROM [TrainingPlans] AS p
                INNER JOIN [Members] AS m ON m.[UserId] = p.[MemberUserId]
                WHERE p.[MemberId] IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE p
                SET p.[AssignedByMemberId] = m.[Id]
                FROM [TrainingPlans] AS p
                INNER JOIN [Members] AS m ON m.[UserId] = p.[AssignedByUserId]
                WHERE p.[AssignedByMemberId] IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE c
                SET c.[InstructorMemberId] = m.[Id]
                FROM [Classes] AS c
                INNER JOIN [Members] AS m ON m.[UserId] = c.[InstructorUserId]
                WHERE c.[InstructorMemberId] IS NULL;
                """);

            // The indexes come down first: a filtered unique index over a column being altered blocks
            // the ALTER, and they are rebuilt below with the filter the required column deserves.
            migrationBuilder.DropIndex(
                name: "IX_TrainingPlans_MemberId_Active",
                table: "TrainingPlans");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Class_MemberId_Active",
                table: "Bookings");

            // NO defaultValue ON ANY OF THESE, unlike what EF scaffolds. A default of
            // 00000000-0000-0000-0000-000000000000 would not help — ALTER COLUMN does not backfill
            // existing NULLs with it — and it would quietly arm a future INSERT to point at a member
            // id that cannot exist, against a Restrict foreign key. If a NULL survives the backfill
            // above, this migration SHOULD fail rather than invent a key.
            migrationBuilder.AlterColumn<Guid>(
                name: "MemberId",
                table: "TrainingPlans",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedByMemberId",
                table: "TrainingPlans",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "InstructorMemberId",
                table: "Classes",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "MemberId",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            // The plain status filter again. The "IS NOT NULL" term these carried existed only
            // because SQL Server treats NULLs as EQUAL for uniqueness, which stopped mattering the
            // moment the column stopped allowing them.
            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_MemberId_Active",
                table: "TrainingPlans",
                column: "MemberId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Class_MemberId_Active",
                table: "Bookings",
                columns: new[] { "ClassId", "MemberId" },
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Loss-free: relaxing NOT NULL back to nullable discards nothing, and the "IS NOT NULL" term
        /// goes back onto the two indexes so the previous artifact — which may write a NULL member
        /// key — is not tripped by NULL-equality on the second such row.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrainingPlans_MemberId_Active",
                table: "TrainingPlans");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Class_MemberId_Active",
                table: "Bookings");

            migrationBuilder.AlterColumn<Guid>(
                name: "MemberId",
                table: "TrainingPlans",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "AssignedByMemberId",
                table: "TrainingPlans",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "InstructorMemberId",
                table: "Classes",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "MemberId",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_MemberId_Active",
                table: "TrainingPlans",
                column: "MemberId",
                unique: true,
                filter: "[Status] = 0 AND [MemberId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Class_MemberId_Active",
                table: "Bookings",
                columns: new[] { "ClassId", "MemberId" },
                unique: true,
                filter: "[Status] = 0 AND [MemberId] IS NOT NULL");
        }
    }
}
