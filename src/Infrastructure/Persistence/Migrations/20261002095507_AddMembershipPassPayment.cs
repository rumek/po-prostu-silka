using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipPassPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PaidAt",
                table: "MembershipPasses",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaidRecordedBy",
                table: "MembershipPasses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            // Every karnet that exists before this column counts as paid on its club-local issue day, so
            // nobody turns into a debtor on deploy. PaidRecordedBy stays null: nobody recorded these.
            //
            // AT TIME ZONE with the WINDOWS zone name — SQL Server (and Azure SQL) does not take IANA
            // names — converting the datetimeoffset to Europe/Warsaw before taking the date. A plain
            // CAST([IssuedAt] AS date) would take the UTC date and put every pass issued between local
            // midnight and 01:00/02:00 on the previous day.
            migrationBuilder.Sql("""
                UPDATE [MembershipPasses]
                SET [PaidAt] = CAST([IssuedAt] AT TIME ZONE 'Central European Standard Time' AS date)
                WHERE [PaidAt] IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "MembershipPasses");

            migrationBuilder.DropColumn(
                name: "PaidRecordedBy",
                table: "MembershipPasses");
        }
    }
}
