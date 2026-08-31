using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// TEMPORARY - deliberately broken migration used to verify plan criteria 3.8 and 3.9:
    /// that a failing migration aborts the pipeline BEFORE azure/webapps-deploy runs, and
    /// that the JIT firewall cleanup still fires via `if: always()`.
    ///
    /// This is reverted immediately after the test run. It must never survive on main.
    /// </summary>
    public partial class DeliberatelyBrokenTest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Invalid on purpose: the table does not exist, so SQL Server raises an error
            // and EF rolls the migration transaction back - nothing is half-applied.
            migrationBuilder.Sql("INSERT INTO NoSuchTable_DeliberateFailure (Id) VALUES (1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
