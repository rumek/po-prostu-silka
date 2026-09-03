using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates the Bookings table and adds Classes.ConcurrencyStamp - the schema half of S-08's
    /// no-overbooking guarantee (prd.md US-01, FR-008, FR-009).
    ///
    /// <para>
    /// THE STAMP IS THE MECHANISM, THE INDEX IS THE BACKSTOP. Serialization of concurrent bookings
    /// comes from the concurrency token on Classes: every booking write rotates it, so EF puts it in
    /// the WHERE clause of an UPDATE and the loser of a race is told to re-read.
    /// IX_Bookings_Class_Member_Active is defence in depth for the narrower double-booking case - it
    /// is what still holds if a future write path forgets to rotate the stamp.
    /// </para>
    ///
    /// <para>
    /// The index is FILTERED to [Status] = 0 (Active) rather than plain, because FR-009 keeps a
    /// cancelled booking in history and the member may book the same class again. A plain unique
    /// index would reject that second booking forever. Two consequences follow: BookingStatus.Active
    /// must stay 0, and any session issuing DML against Bookings needs SQL Server''s required SET
    /// options - EF Core''s connections set them, a hand-run raw session may not.
    /// </para>
    ///
    /// <para>
    /// ConcurrencyStamp is added NOT NULL with a NEWID()-derived default so each existing Classes row
    /// gets its OWN token rather than sharing one; a shared value would make every class collide with
    /// every other on the first write. The default stays on the column afterwards and is never used -
    /// EF always supplies the value.
    /// </para>
    ///
    /// <para>
    /// FULLY REVERSIBLE, and no exception to AGENTS.md''s one-release lag is taken here: Down drops a
    /// table and a column that only this release''s code reads. A rollback to the previous artifact is
    /// safe even WITHOUT running Down - an extra table and an extra defaulted column break none of
    /// its queries.
    /// </para>
    /// </summary>
    public partial class AddBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                table: "Classes",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: false,
                // NOT defaultValue: "" - that would hand every existing row the SAME token, and two
                // classes sharing a stamp is not a correctness bug but it is a needless collision.
                // CONVERT because NEWID() is a uniqueidentifier and the column is nvarchar(36), which
                // is exactly the width of a GUID in its dashed form.
                defaultValueSql: "CONVERT(nvarchar(36), NEWID())");

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClassId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookings_AspNetUsers_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Class_Member_Active",
                table: "Bookings",
                columns: new[] { "ClassId", "MemberUserId" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Member_Status",
                table: "Bookings",
                columns: new[] { "MemberUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Classes");
        }
    }
}
