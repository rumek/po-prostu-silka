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
        protected override void Down(MigrationBuilder migrationBuilder)
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
                nullable: false,
                defaultValue: "",
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
                defaultValue: "",
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
                defaultValue: "",
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
                defaultValue: "",
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
