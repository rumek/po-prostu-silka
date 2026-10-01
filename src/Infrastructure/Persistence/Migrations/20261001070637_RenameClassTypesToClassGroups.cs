using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// S-33: class types are called groups. HAND-WRITTEN, never accepted as scaffolded: EF cannot tell
    /// a renamed entity from a new one, and scaffolds a drop of the old table and a create of the new
    /// one, which on the only database there is would delete every group and, through the foreign
    /// key, every class. Every step below is a rename in place, so no row moves.
    ///
    /// ACCEPTED ONCE, staging only (user, 2026-10-01): migrations run before the deploy, so the
    /// previous build serves against these names for a few minutes, and rollback.yml cannot restore
    /// the previous artifact without first running this Down by hand. Not a precedent.
    /// </summary>
    public partial class RenameClassTypesToClassGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Classes_ClassTypes_ClassTypeId",
                table: "Classes");

            migrationBuilder.RenameTable(
                name: "ClassTypes",
                newName: "ClassGroups");

            // EF has no RenamePrimaryKey. A constraint's sp_rename name must be schema-qualified,
            // and the schema is the table's, whatever the connecting user's default is.
            migrationBuilder.Sql(
                "DECLARE @pk nvarchar(300) = QUOTENAME(OBJECT_SCHEMA_NAME(OBJECT_ID(N'[ClassGroups]'))) + N'.[PK_ClassTypes]';\n" +
                "EXEC sp_rename @pk, N'PK_ClassGroups', N'OBJECT';");

            migrationBuilder.RenameColumn(
                name: "ClassTypeId",
                table: "Classes",
                newName: "ClassGroupId");

            migrationBuilder.RenameIndex(
                name: "IX_Classes_ClassTypeId",
                table: "Classes",
                newName: "IX_Classes_ClassGroupId");

            migrationBuilder.RenameIndex(
                name: "IX_ClassTypes_Name_Active",
                table: "ClassGroups",
                newName: "IX_ClassGroups_Name_Active");

            migrationBuilder.AddForeignKey(
                name: "FK_Classes_ClassGroups_ClassGroupId",
                table: "Classes",
                column: "ClassGroupId",
                principalTable: "ClassGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Classes_ClassGroups_ClassGroupId",
                table: "Classes");

            migrationBuilder.RenameIndex(
                name: "IX_ClassGroups_Name_Active",
                table: "ClassGroups",
                newName: "IX_ClassTypes_Name_Active");

            migrationBuilder.RenameIndex(
                name: "IX_Classes_ClassGroupId",
                table: "Classes",
                newName: "IX_Classes_ClassTypeId");

            migrationBuilder.RenameColumn(
                name: "ClassGroupId",
                table: "Classes",
                newName: "ClassTypeId");

            migrationBuilder.Sql(
                "DECLARE @pk nvarchar(300) = QUOTENAME(OBJECT_SCHEMA_NAME(OBJECT_ID(N'[ClassGroups]'))) + N'.[PK_ClassGroups]';\n" +
                "EXEC sp_rename @pk, N'PK_ClassTypes', N'OBJECT';");

            migrationBuilder.RenameTable(
                name: "ClassGroups",
                newName: "ClassTypes");

            migrationBuilder.AddForeignKey(
                name: "FK_Classes_ClassTypes_ClassTypeId",
                table: "Classes",
                column: "ClassTypeId",
                principalTable: "ClassTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
