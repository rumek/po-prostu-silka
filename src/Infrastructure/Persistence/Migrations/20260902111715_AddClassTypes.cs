using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClassTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Development-only data, discarded rather than migrated (prd-v2.md, Constraints &
            // Compatibility). S-06 builds occurrences from class types, and generating types out of
            // retyped names would be work spent on rows nobody booked.
            //
            // NARROW BY DESIGN: Classes only. Accounts, roles, statuses, push subscriptions, the
            // notification outbox and training plans are all untouched, and no Bookings table exists
            // yet (Booking arrives in S-08).
            //
            // ONE-WAY. Down below reverses the SCHEMA in full, but cannot bring these rows back.
            // This is the single place this change departs from the repository's reversibility rule,
            // and the departure is deliberate: no real club is using the application, so there is
            // nothing here to lose. Read it as a decision, not an oversight.
            //
            // Runs FIRST, before ClassTypeId becomes a foreign key. The column is nullable today so
            // the order is not yet load-bearing - it becomes so the moment S-06 tightens it.
            migrationBuilder.Sql("DELETE FROM [Classes];");

            migrationBuilder.AddColumn<Guid>(
                name: "ClassTypeId",
                table: "Classes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClassTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DefaultDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    DefaultCapacity = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Classes_ClassTypeId",
                table: "Classes",
                column: "ClassTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassTypes_Name_Active",
                table: "ClassTypes",
                column: "Name",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_Classes_ClassTypes_ClassTypeId",
                table: "Classes",
                column: "ClassTypeId",
                principalTable: "ClassTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Classes_ClassTypes_ClassTypeId",
                table: "Classes");

            migrationBuilder.DropTable(
                name: "ClassTypes");

            migrationBuilder.DropIndex(
                name: "IX_Classes_ClassTypeId",
                table: "Classes");

            migrationBuilder.DropColumn(
                name: "ClassTypeId",
                table: "Classes");
        }
    }
}
