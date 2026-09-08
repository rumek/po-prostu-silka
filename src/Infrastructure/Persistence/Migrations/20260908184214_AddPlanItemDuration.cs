using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po_prostu_silka.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Adds the duration a trainer prescribes for an exercise measured in time rather than in load —
    /// a plank, a hollow hold, a farmer's walk. It sits on the plan ITEM beside WeightKg and
    /// RestSeconds, not on Exercise: the same plank is 45 s in one member's plan and 60 s in
    /// another's.
    ///
    /// <para>
    /// ADDITIVE AND NULLABLE, so the previously deployed artifact keeps running against this schema —
    /// which is what the deploy workflow requires, since migrations apply before the new artifact
    /// ships. There is no backfill and none would be meaningful: absent IS the correct duration for
    /// every prescription written before this column existed.
    /// </para>
    ///
    /// <para>
    /// The Down is a plain DropColumn and genuinely reverses this migration. It is lossy in exactly
    /// one way, and it is the ordinary cost of reversing an added column: durations entered while it
    /// was applied do not survive the rollback. Nothing else depends on the column — no index, no
    /// foreign key, no filtered predicate — so there is nothing else to unwind.
    /// </para>
    /// </summary>
    public partial class AddPlanItemDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "TrainingPlanItems",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "TrainingPlanItems");
        }
    }
}
