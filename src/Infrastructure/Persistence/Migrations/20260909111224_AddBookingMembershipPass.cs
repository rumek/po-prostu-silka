using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingMembershipPass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MembershipPassId",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_MembershipPassId_Status",
                table: "Bookings",
                columns: new[] { "MembershipPassId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_MembershipPasses_MembershipPassId",
                table: "Bookings",
                column: "MembershipPassId",
                principalTable: "MembershipPasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_MembershipPasses_MembershipPassId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_MembershipPassId_Status",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "MembershipPassId",
                table: "Bookings");
        }
    }
}
