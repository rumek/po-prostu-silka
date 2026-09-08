using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// S-14 Phase 9: the account columns nothing has read or written for a release finally go.
    ///
    /// <para>
    /// FOUR FOREIGN KEYS — <c>Bookings.MemberUserId</c>, <c>TrainingPlans.MemberUserId</c> and
    /// <c>AssignedByUserId</c>, <c>Classes.InstructorUserId</c> — plus the four address columns on
    /// <c>AspNetUsers</c>. <c>PhoneNumber</c> STAYS: it is Identity's own inherited column, not
    /// something this slice added, and the member's copy sits beside it rather than replacing it.
    /// </para>
    ///
    /// <para>
    /// DESTRUCTIVE, AND THEREFORE ONE RELEASE LATE. Rollback redeploys the previous artifact but does
    /// not roll back schema (AGENTS.md), so this could only run once the artifact that still READ
    /// these columns was two releases behind — the same reasoning
    /// <c>20260902165516_DropDeadClassColumns</c> set the precedent for.
    /// </para>
    /// </summary>
    public partial class DropLegacyUserColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_AspNetUsers_MemberUserId",
                table: "Bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_Classes_AspNetUsers_InstructorUserId",
                table: "Classes");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingPlans_AspNetUsers_AssignedByUserId",
                table: "TrainingPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_TrainingPlans_AspNetUsers_MemberUserId",
                table: "TrainingPlans");

            migrationBuilder.DropIndex(
                name: "IX_TrainingPlans_AssignedByUserId",
                table: "TrainingPlans");

            migrationBuilder.DropIndex(
                name: "IX_TrainingPlans_Member_Active",
                table: "TrainingPlans");

            migrationBuilder.DropIndex(
                name: "IX_Classes_InstructorUserId",
                table: "Classes");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Class_Member_Active",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Member_Status",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "AssignedByUserId",
                table: "TrainingPlans");

            migrationBuilder.DropColumn(
                name: "MemberUserId",
                table: "TrainingPlans");

            migrationBuilder.DropColumn(
                name: "InstructorUserId",
                table: "Classes");

            migrationBuilder.DropColumn(
                name: "MemberUserId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "City",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HouseNumber",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "AspNetUsers");
        }

        /// <summary>
        /// Re-adds the columns nullable and backfills them from <c>Members</c>.
        ///
        /// <para>
        /// LOSSY, AND UNAVOIDABLY SO: a booking, plan or class belonging to a member with no account
        /// has no account id to restore, and comes back NULL. That is exactly why every one of these
        /// columns was made nullable before the write side stopped filling it — the previous artifact
        /// tolerates the NULLs this leaves behind. The address on <c>AspNetUsers</c> is restored only
        /// for accounts whose member row still holds one.
        /// </para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedByUserId",
                table: "TrainingPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MemberUserId",
                table: "TrainingPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructorUserId",
                table: "Classes",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MemberUserId",
                table: "Bookings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HouseNumber",
                table: "AspNetUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "AspNetUsers",
                type: "nvarchar(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // BEFORE THE INDEXES, for the reason this slice has now hit three times: a filtered
            // unique index over an all-NULL column sees every row as equal, because SQL Server
            // treats NULLs as EQUAL for uniqueness. The two indexes below carry an IS NOT NULL term
            // that would save us anyway; ordering it this way means we are not relying on that.
            migrationBuilder.Sql("""
                UPDATE b
                SET b.[MemberUserId] = m.[UserId]
                FROM [Bookings] AS b
                INNER JOIN [Members] AS m ON m.[Id] = b.[MemberId];
                """);

            migrationBuilder.Sql("""
                UPDATE p
                SET p.[MemberUserId] = m.[UserId]
                FROM [TrainingPlans] AS p
                INNER JOIN [Members] AS m ON m.[Id] = p.[MemberId];
                """);

            migrationBuilder.Sql("""
                UPDATE p
                SET p.[AssignedByUserId] = m.[UserId]
                FROM [TrainingPlans] AS p
                INNER JOIN [Members] AS m ON m.[Id] = p.[AssignedByMemberId];
                """);

            migrationBuilder.Sql("""
                UPDATE c
                SET c.[InstructorUserId] = m.[UserId]
                FROM [Classes] AS c
                INNER JOIN [Members] AS m ON m.[Id] = c.[InstructorMemberId];
                """);

            migrationBuilder.Sql("""
                UPDATE u
                SET u.[Street] = m.[Street],
                    u.[HouseNumber] = m.[HouseNumber],
                    u.[PostalCode] = m.[PostalCode],
                    u.[City] = m.[City]
                FROM [AspNetUsers] AS u
                INNER JOIN [Members] AS m ON m.[UserId] = u.[Id];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_AssignedByUserId",
                table: "TrainingPlans",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_Member_Active",
                table: "TrainingPlans",
                column: "MemberUserId",
                unique: true,
                filter: "[Status] = 0 AND [MemberUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_InstructorUserId",
                table: "Classes",
                column: "InstructorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Class_Member_Active",
                table: "Bookings",
                columns: new[] { "ClassId", "MemberUserId" },
                unique: true,
                filter: "[Status] = 0 AND [MemberUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Member_Status",
                table: "Bookings",
                columns: new[] { "MemberUserId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_AspNetUsers_MemberUserId",
                table: "Bookings",
                column: "MemberUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Classes_AspNetUsers_InstructorUserId",
                table: "Classes",
                column: "InstructorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingPlans_AspNetUsers_AssignedByUserId",
                table: "TrainingPlans",
                column: "AssignedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TrainingPlans_AspNetUsers_MemberUserId",
                table: "TrainingPlans",
                column: "MemberUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
