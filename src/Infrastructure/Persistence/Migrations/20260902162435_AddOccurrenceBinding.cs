using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// S-06: an occurrence becomes an INSTANCE of a class type (prd-v2 FR-008 - FR-012).
    ///
    /// <para>
    /// NO DATA STATEMENTS, AND NONE ARE NEEDED. AddClassTypes already emptied Classes
    /// ("DELETE FROM [Classes] WHERE [ClassTypeId] IS NULL"), so tightening ClassTypeId and adding a
    /// required InstructorUserId need no backfill and lose nothing. Both directions are safe on an
    /// empty table; the scaffolder's data-loss warning refers to columns that hold no rows.
    /// </para>
    ///
    /// <para>
    /// THREE COLUMNS ARE DELIBERATELY NOT DROPPED: Name, Room and Instructor. Nothing reads or writes
    /// them after this migration - the name and description resolve through ClassTypes and the
    /// instructor through AspNetUsers - but AGENTS.md's rule is that rollback redeploys the previous
    /// artifact WITHOUT rolling back the schema, so the previous build must still find the columns it
    /// INSERTs. Dropping them belongs to a follow-up change, one release later. They are relaxed to
    /// NULL here rather than left NOT NULL because the new build stops supplying them.
    /// </para>
    ///
    /// <para>
    /// WHAT ROLLBACK DOES AND DOES NOT BUY. Relaxing those three keeps the previous build's READS and
    /// its INSERT column list valid. It cannot make the previous build's class CREATION succeed: that
    /// build supplies neither ClassTypeId nor InstructorUserId, and both are now NOT NULL behind a
    /// foreign key, so a create would fail on the constraint. That is inherent to making the
    /// references required and is accepted - after a rollback the schedule still reads, and creating
    /// classes waits for the roll-forward.
    /// </para>
    /// </summary>
    public partial class AddOccurrenceBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IX_Classes_Room_StartsAt served the old room-scoped overlap check. The rule widened to
            // the whole club (FR-012), so its equality half is gone and IX_Classes_StartsAt below
            // takes over as a pure range scan.
            migrationBuilder.DropIndex(
                name: "IX_Classes_Room_StartsAt",
                table: "Classes");

            migrationBuilder.AlterColumn<string>(
                name: "Room",
                table: "Classes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Classes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Instructor",
                table: "Classes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<Guid>(
                name: "ClassTypeId",
                table: "Classes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructorUserId",
                table: "Classes",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_InstructorUserId",
                table: "Classes",
                column: "InstructorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_StartsAt",
                table: "Classes",
                column: "StartsAt");

            migrationBuilder.AddForeignKey(
                name: "FK_Classes_AspNetUsers_InstructorUserId",
                table: "Classes",
                column: "InstructorUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Classes_AspNetUsers_InstructorUserId",
                table: "Classes");

            migrationBuilder.DropIndex(
                name: "IX_Classes_InstructorUserId",
                table: "Classes");

            migrationBuilder.DropIndex(
                name: "IX_Classes_StartsAt",
                table: "Classes");

            migrationBuilder.DropColumn(
                name: "InstructorUserId",
                table: "Classes");

            migrationBuilder.AlterColumn<string>(
                name: "Room",
                table: "Classes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Classes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Instructor",
                table: "Classes",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ClassTypeId",
                table: "Classes",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_Classes_Room_StartsAt",
                table: "Classes",
                columns: new[] { "Room", "StartsAt" });
        }
    }
}
