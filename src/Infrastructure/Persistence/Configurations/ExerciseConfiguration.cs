using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

public class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        builder.ToTable("Exercises");
        builder.HasKey(x => x.Id);

        // 200 matches ClassType.Name - a label an admin types, seen in a list.
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);

        // Every column below is nullable, and nullable ONLY because the CLR type is - there is no
        // IsRequired(false) anywhere in this codebase. The lengths are what the endpoint's
        // validation mirrors; the two must stay in step, or an over-long value becomes a 500 at the
        // database instead of a 400 on the field.
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.MuscleGroup).HasMaxLength(100);
        builder.Property(x => x.Difficulty).HasMaxLength(50);
        builder.Property(x => x.Equipment).HasMaxLength(200);
        builder.Property(x => x.Preparation).HasMaxLength(2000);
        builder.Property(x => x.StartingPosition).HasMaxLength(2000);

        // The longest field by design: this is what a member reads mid-set in S-11.
        builder.Property(x => x.Execution).HasMaxLength(4000);

        // The id is 11 characters. 20 is headroom against YouTube ever lengthening it, bought at the
        // price of nothing - it costs no storage in a varchar and saves a migration.
        builder.Property(x => x.VideoId).HasMaxLength(20);

        builder.Property(x => x.CreatedAt).IsRequired();

        // Defaulting to true means a row inserted without an explicit flag is OFFERED, never
        // silently hidden - the same reasoning ClassTypeConfiguration applies.
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        // FILTERED, not plain. Uniqueness holds among ACTIVE exercises only: without the filter,
        // deactivating an entry would hold its name hostage forever and the admin could never
        // re-create it.
        //
        // This index is what actually closes the race the endpoint's pre-check only narrows - two
        // simultaneous creates both pass the check, and the second write fails here rather than
        // producing two active exercises with one name.
        builder
            .HasIndex(x => x.Name)
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("IX_Exercises_Name_Active");
    }
}
