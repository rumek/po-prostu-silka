using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Moves the club's foreign keys off the Identity account and onto <c>Members</c> — bookings, both
    /// ends of a training plan, and a class's instructor.
    ///
    /// <para>
    /// ADDITIVE, AND THE OLD COLUMNS STAY. The new columns are nullable, backfilled here, and written
    /// in parallel with the old ones for one release; the reads move in the next one and the old
    /// columns go in the one after that. That sequence is what the deploy workflow requires — it
    /// applies migrations BEFORE the new build ships, so the previous artifact has to keep working
    /// against this schema, and it only knows the old columns.
    /// </para>
    ///
    /// <para>
    /// THE BACKFILL RUNS BEFORE THE UNIQUE INDEXES ARE CREATED, which is not cosmetic: an index built
    /// over a column that is still NULL everywhere would see every row as equal (SQL Server treats
    /// NULLs as equal for uniqueness) and the CREATE would fail. Ordering is why the generated
    /// statements below are re-sequenced by hand rather than left as EF emitted them.
    /// </para>
    /// </summary>
    public partial class AddMemberForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedByMemberId",
                table: "TrainingPlans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MemberId",
                table: "TrainingPlans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InstructorMemberId",
                table: "Classes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MemberId",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: true);

            // Every existing row has an account behind it, and AddMembers gave every account a member,
            // so each of these joins resolves for every row. Written as UPDATE..FROM rather than a
            // correlated subquery because that is what SQL Server optimises for a full-table backfill.
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

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_AssignedByMemberId",
                table: "TrainingPlans",
                column: "AssignedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_MemberId_Active",
                table: "TrainingPlans",
                column: "MemberId",
                unique: true,
                filter: "[Status] = 0 AND [MemberId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_InstructorMemberId",
                table: "Classes",
                column: "InstructorMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Class_MemberId_Active",
                table: "Bookings",
                columns: new[] { "ClassId", "MemberId" },
                unique: true,
                filter: "[Status] = 0 AND [MemberId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_MemberId_Status",
                table: "Bookings",
                columns: new[] { "MemberId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Members_MemberId",
                table: "Bookings",
                column: "MemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Classes_Members_InstructorMemberId",
                table: "Classes",
                column: "InstructorMemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingPlans_Members_AssignedByMemberId",
                table: "TrainingPlans",
                column: "AssignedByMemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingPlans_Members_MemberId",
                table: "TrainingPlans",
                column: "MemberId",
                principalTable: "Members",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Drops the new keys and leaves the account columns, which never stopped being written at this
        /// phase — so reverting loses nothing.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Members_MemberId",
                table: "Bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_Classes_Members_InstructorMemberId",
                table: "Classes");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingPlans_Members_AssignedByMemberId",
                table: "TrainingPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingPlans_Members_MemberId",
                table: "TrainingPlans");

            migrationBuilder.DropIndex(
                name: "IX_TrainingPlans_AssignedByMemberId",
                table: "TrainingPlans");

            migrationBuilder.DropIndex(
                name: "IX_TrainingPlans_MemberId_Active",
                table: "TrainingPlans");

            migrationBuilder.DropIndex(
                name: "IX_Classes_InstructorMemberId",
                table: "Classes");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Class_MemberId_Active",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_MemberId_Status",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "AssignedByMemberId",
                table: "TrainingPlans");

            migrationBuilder.DropColumn(
                name: "MemberId",
                table: "TrainingPlans");

            migrationBuilder.DropColumn(
                name: "InstructorMemberId",
                table: "Classes");

            migrationBuilder.DropColumn(
                name: "MemberId",
                table: "Bookings");
        }
    }
}
