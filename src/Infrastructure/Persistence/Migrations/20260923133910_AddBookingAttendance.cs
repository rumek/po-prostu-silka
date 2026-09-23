using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attendance",
                table: "Bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AttendanceRecordedAt",
                table: "Bookings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttendanceRecordedBy",
                table: "Bookings",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attendance",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "AttendanceRecordedAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "AttendanceRecordedBy",
                table: "Bookings");
        }
    }
}
