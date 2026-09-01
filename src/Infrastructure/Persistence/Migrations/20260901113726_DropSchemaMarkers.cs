using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Closes F-02's deferred-destructive handoff.
    ///
    /// F-01 created SchemaMarkers to prove the migration pipeline. F-02 deleted the C# type but
    /// deliberately left the table, because rollback on B1 redeploys the previous artifact without
    /// rolling back schema (infrastructure.md:85) - so a destructive change must lag one release
    /// behind the code that stops needing it. That release is this one.
    ///
    /// BOTH DIRECTIONS ARE HAND-WRITTEN. EF generates nothing here: the entity left the model in
    /// F-02, so from EF's perspective the table is already gone and there is no model delta to
    /// scaffold. The Down below reproduces F-01's original shape exactly
    /// (20260831144519_InitialSchemaMarker), because a migration that destroys something is the last
    /// place an empty Down is acceptable.
    /// </summary>
    public partial class DropSchemaMarkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchemaMarkers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SchemaMarkers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchemaMarkers", x => x.Id);
                });
        }
    }
}
