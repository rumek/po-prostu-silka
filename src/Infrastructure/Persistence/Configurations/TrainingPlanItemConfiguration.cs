using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

public class TrainingPlanItemConfiguration : IEntityTypeConfiguration<TrainingPlanItem>
{
    public void Configure(EntityTypeBuilder<TrainingPlanItem> builder)
    {
        builder.ToTable("TrainingPlanItems");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Position).IsRequired();

        // Nullable ONLY because the CLR type is - there is no IsRequired(false) anywhere in this
        // codebase. The lengths and ranges are what the endpoint's validation and the Angular
        // validators mirror; the three must stay in step.
        builder.Property(x => x.Sets);
        builder.Property(x => x.RestSeconds);
        builder.Property(x => x.Reps).HasMaxLength(50);
        builder.Property(x => x.Note).HasMaxLength(500);

        // THE SCHEMA'S FIRST DECIMAL, and therefore the precedent every later one is read against.
        // decimal(5,2) holds up to 999.99 kg in 0.01 steps: enough for plate math, microplates, and
        // any load a person will move. Set explicitly rather than left to EF's decimal(18,2) default,
        // which would work but would establish an accidental convention nobody chose.
        builder.Property(x => x.WeightKg).HasPrecision(5, 2);

        // CASCADE, the only one in this codebase besides PushSubscription, and for the same reason:
        // an item has no life outside its plan. Nothing deletes a plan today - assignment archives -
        // but if a plan ever is deleted, its items must go with it rather than becoming unreachable
        // rows pointing at nothing.
        builder
            .HasOne<TrainingPlan>()
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.TrainingPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // RESTRICT, and this is the foreign key S-10's deactivate-instead-of-delete decision exists
        // for: a deleted exercise would either orphan plan rows or be refused here, and the library
        // deliberately has no delete endpoint at all.
        builder
            .HasOne(x => x.Exercise)
            .WithMany()
            .HasForeignKey(x => x.ExerciseId)
            .OnDelete(DeleteBehavior.Restrict);

        // NOT UNIQUE, deliberately. Positions are unique within a plan by construction - every write
        // clears the plan's items and re-inserts them numbered from the request array - so a unique
        // index would buy nothing today and would break the first future write path that reorders
        // rows one UPDATE at a time, since SQL Server checks a unique index per statement rather than
        // per transaction. This index earns its place as the seek the plan projection uses to fetch a
        // plan's items in order.
        builder
            .HasIndex(x => new { x.TrainingPlanId, x.Position })
            .HasDatabaseName("IX_TrainingPlanItems_Plan_Position");
    }
}
