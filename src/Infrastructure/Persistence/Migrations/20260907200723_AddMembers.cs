using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Introduces <c>Members</c> — the club's record of a person — and backfills one row per existing
    /// account, so that after this migration every account has a member and no code yet reads one.
    ///
    /// <para>
    /// ADDITIVE ONLY, AND THAT IS THE POINT. The deploy workflow applies migrations BEFORE the new
    /// artifact ships ("schema is always &gt;= code, never behind it"), so the previous artifact keeps
    /// running against this schema — it simply never looks at the new table. The foreign keys that
    /// currently point at AspNetUsers are moved by a later migration, once the code that reads them has
    /// shipped.
    /// </para>
    /// </summary>
    public partial class AddMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Street = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HouseNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AccessCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    AccessCodeExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Members_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Members_AccessCode",
                table: "Members",
                column: "AccessCode",
                unique: true,
                filter: "[AccessCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Members_Email",
                table: "Members",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Members_Status",
                table: "Members",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Members_UserId",
                table: "Members",
                column: "UserId",
                unique: true,
                filter: "[UserId] IS NOT NULL");

            // ONE MEMBER PER EXISTING ACCOUNT. Without this every account in the database would be a
            // person the club has no record of, and the authorization claim a later phase adds would
            // then refuse them — including the admin, which is how a club locks itself out of its own
            // app. This is the first of the three producers that make that unreachable; the other two
            // are AdminSeeder and AuthEndpoints.RegisterAsync.
            //
            // WHERE NOT EXISTS makes it a no-op on re-run. The deploy applies an idempotent script, and
            // a data statement inside a schema migration does not get that guarantee for free.
            //
            // The status mapping is deliberate and is NOT the identity mapping:
            //   account Blocked (2) -> membership Blocked (1)
            //   account Pending (0) or Active (1) -> membership Active (0)
            // Pending is a fact about a LOGIN awaiting approval, and it stays on the account, which is
            // what still refuses them at the door. The club's own record of that person is not pending
            // anything — see MembershipStatus for why there is no membership Pending at all.
            //
            // Contact details are copied rather than moved. Both rows carry them until the read flips,
            // which is what lets the previous artifact keep serving profiles from AspNetUsers.
            migrationBuilder.Sql("""
                INSERT INTO [Members]
                    ([Id], [UserId], [DisplayName], [Email], [PhoneNumber], [Street], [HouseNumber],
                     [PostalCode], [City], [Status], [CreatedAt], [ConcurrencyStamp])
                SELECT
                    NEWID(),
                    u.[Id],
                    u.[DisplayName],
                    u.[Email],
                    u.[PhoneNumber],
                    u.[Street],
                    u.[HouseNumber],
                    u.[PostalCode],
                    u.[City],
                    CASE WHEN u.[Status] = 2 THEN 1 ELSE 0 END,
                    u.[CreatedAt],
                    CONVERT(nvarchar(36), NEWID())
                FROM [AspNetUsers] AS u
                WHERE NOT EXISTS (SELECT 1 FROM [Members] AS m WHERE m.[UserId] = u.[Id]);
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// LOSSY, AND UNAVOIDABLY SO: dropping this table discards every member who has no account,
        /// because there is nowhere else in the schema to put them. Accounts themselves are untouched.
        /// The same shape as DropDeadClassColumns' Down — a reversal that restores the schema, not the
        /// data that only the new schema could hold.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Members");
        }
    }
}
