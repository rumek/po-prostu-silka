using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Lets the legacy account columns hold NULL, and excludes NULLs from the two filtered unique
    /// indexes over them.
    ///
    /// <para>
    /// FORCED BY THE SLICE ITSELF, and earlier than the plan expected. A member with no account has no
    /// Identity id to put in <c>MemberUserId</c> / <c>InstructorUserId</c> / <c>AssignedByUserId</c> —
    /// so the moment such a person can be booked into a class or given a plan, which is what S-14 is
    /// for, those columns cannot stay NOT NULL. Postponing this to the contract migration would have
    /// meant shipping a feature that throws a foreign-key violation the first time anyone uses it.
    /// </para>
    ///
    /// <para>
    /// THE INDEX FILTERS ARE THE OTHER HALF, and are easy to miss: SQL Server treats NULLs as EQUAL
    /// for uniqueness, so a unique index over a now-nullable column would reject the SECOND
    /// accountless member booked into any class, or given any plan. Both filters gain an
    /// "IS NOT NULL" term.
    /// </para>
    ///
    /// <para>
    /// Still backwards compatible: every row the previous artifact can create still populates these
    /// columns, because that artifact only knows how to act for accounts.
    /// </para>
    /// </summary>
    public partial class AllowLegacyUserColumnsToBeNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrainingPlans_Member_Active",
                table: "TrainingPlans");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Class_Member_Active",
                table: "Bookings");

            migrationBuilder.AlterColumn<string>(
                name: "MemberUserId",
                table: "TrainingPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<string>(
                name: "AssignedByUserId",
                table: "TrainingPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<string>(
                name: "InstructorUserId",
                table: "Classes",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<string>(
                name: "MemberUserId",
                table: "Bookings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_Member_Active",
                table: "TrainingPlans",
                column: "MemberUserId",
                unique: true,
                filter: "[Status] = 0 AND [MemberUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Class_Member_Active",
                table: "Bookings",
                columns: new[] { "ClassId", "MemberUserId" },
                unique: true,
                filter: "[Status] = 0 AND [MemberUserId] IS NOT NULL");
        }

        /// <inheritdoc />
        /// <summary>
        /// LOSSY, in exactly one place, and it has to be: a booking or a plan belonging to a member
        /// with no account has no Identity id to put back, so it cannot exist in the schema this
        /// reverses to. Those rows are DELETED rather than defaulted.
        ///
        /// <para>
        /// NO <c>defaultValue</c> ON ANY OF THE FOUR ALTERS, against what EF scaffolds — the same
        /// refusal <c>RequireMemberForeignKeys</c> makes, for a reason that bites harder here. EF's
        /// <c>N''</c> default is not inert: it emits an <c>UPDATE … SET N'' WHERE … IS NULL</c> ahead
        /// of the ALTER, and by the time this runs in a full unwind, <c>DropLegacyUserColumns.Down</c>
        /// has already re-added the foreign keys to <c>AspNetUsers</c>. An empty string is not a valid
        /// <c>AspNetUsers.Id</c>, so the UPDATE fails the constraint and the rollback aborts mid-chain.
        /// Were it to survive, the two unique indexes rebuilt below would then see every accountless
        /// row as the same <c>''</c> member and reject the second one.
        /// </para>
        ///
        /// <para>
        /// A class with no instructor account THROWS instead of being deleted. It is unreachable by
        /// construction — <c>ValidateInstructorAsync</c> has always required an active account holding
        /// Trainer — and a class cannot be removed without taking everyone's bookings with it. If one
        /// is ever found here, stopping is the right answer.
        /// </para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrainingPlans_Member_Active",
                table: "TrainingPlans");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Class_Member_Active",
                table: "Bookings");

            // BEFORE the ALTERs, or every one of them fails on the NULLs. Plan items go with their
            // plan on the cascade TrainingPlanItemConfiguration declares.
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [Classes] WHERE [InstructorUserId] IS NULL)
                    THROW 50000, N'Cannot roll back: a class is taught by a member with no account, and reverting would have to delete the class and every booking on it.', 1;

                DELETE FROM [Bookings] WHERE [MemberUserId] IS NULL;

                DELETE FROM [TrainingPlans] WHERE [MemberUserId] IS NULL OR [AssignedByUserId] IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "MemberUserId",
                table: "TrainingPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AssignedByUserId",
                table: "TrainingPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "InstructorUserId",
                table: "Classes",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MemberUserId",
                table: "Bookings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingPlans_Member_Active",
                table: "TrainingPlans",
                column: "MemberUserId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Class_Member_Active",
                table: "Bookings",
                columns: new[] { "ClassId", "MemberUserId" },
                unique: true,
                filter: "[Status] = 0");
        }
    }
}
