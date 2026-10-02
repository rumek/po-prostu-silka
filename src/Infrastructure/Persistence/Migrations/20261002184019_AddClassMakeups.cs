using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClassMakeups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MakeupClosedAt",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MakeupClosedBy",
                table: "Bookings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MakeupForBookingId",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_MakeupForBookingId_Active",
                table: "Bookings",
                column: "MakeupForBookingId",
                unique: true,
                filter: "[Status] = 0 AND [MakeupForBookingId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Bookings_MakeupForBookingId",
                table: "Bookings",
                column: "MakeupForBookingId",
                principalTable: "Bookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Bookings_MakeupForBookingId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_MakeupForBookingId_Active",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "MakeupClosedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "MakeupClosedBy",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "MakeupForBookingId",
                table: "Bookings");
        }
    }
}
