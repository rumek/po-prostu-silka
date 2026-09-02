using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Removes the three columns S-06 stopped using: Name, Room and the free-text Instructor.
    ///
    /// <para>
    /// A DELIBERATE EXCEPTION TO THE ONE-RELEASE LAG, decided by the product owner with the cost
    /// stated. AGENTS.md requires destructive schema changes to trail the code that stopped needing
    /// them by one release, because rollback redeploys the previous artifact WITHOUT rolling back the
    /// database. This migration ships in the same release that stopped writing the columns.
    /// </para>
    ///
    /// <para>
    /// WHAT THAT COSTS. If this release is deployed and then rolled back, the pre-S-06 build finds
    /// none of the three columns. It does not degrade partially: ClassScheduleQuery projects Name,
    /// Room and Instructor, so GET /api/classes and the admin list both fail with "invalid column
    /// name" - the member schedule stops rendering entirely, not just class creation. Rolling
    /// forward, or running this migration's Down by hand, is the recovery.
    /// </para>
    ///
    /// <para>
    /// NO DATA IS LOST. The Classes table has been empty since AddClassTypes cleared it, and no real
    /// club is using the application yet - which is what makes the exception defensible at all.
    /// </para>
    ///
    /// <para>
    /// Down restores all three as NULLABLE - the state AddOccurrenceBinding left them in, not the
    /// NOT NULL they carried before S-06. It cannot restore values, and does not need to.
    /// </para>
    /// </summary>
    public partial class DropDeadClassColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Instructor",
                table: "Classes");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Classes");

            migrationBuilder.DropColumn(
                name: "Room",
                table: "Classes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Instructor",
                table: "Classes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Classes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Room",
                table: "Classes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
