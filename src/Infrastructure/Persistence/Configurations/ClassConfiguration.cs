using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

public class ClassConfiguration : IEntityTypeConfiguration<Class>
{
    public void Configure(EntityTypeBuilder<Class> builder)
    {
        builder.ToTable("Classes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);

        // Room and Instructor are short human labels, not free-form prose. 100 is generous for both
        // and keeps the (Room, StartsAt) index key narrow.
        builder.Property(x => x.Room).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Instructor).IsRequired().HasMaxLength(100);

        builder.Property(x => x.StartsAt).IsRequired();
        builder.Property(x => x.DurationMinutes).IsRequired();
        builder.Property(x => x.Capacity).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Stored as int. ClassStatus pins explicit values precisely so this mapping is stable.
        // Defaulting to Scheduled means a row inserted without an explicit status is on the
        // schedule, never silently cancelled.
        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(ClassStatus.Scheduled);

        // The member schedule's window query: Status = Scheduled AND StartsAt within the next 14
        // days, ordered by StartsAt. Status leads because it is the equality predicate; StartsAt
        // then serves both the range scan and the ordering, so the query needs no sort.
        builder.HasIndex(x => new { x.Status, x.StartsAt })
            .HasDatabaseName("IX_Classes_Status_StartsAt");

        // The room-overlap check (FR-011's invariant), which runs once per create/edit and once per
        // duplicated week. Room is the equality half, StartsAt bounds the candidate window before
        // the duration arithmetic is applied.
        builder.HasIndex(x => new { x.Room, x.StartsAt })
            .HasDatabaseName("IX_Classes_Room_StartsAt");

        // The definition this occurrence instantiates. Nullable through S-05 - see Class.ClassTypeId.
        //
        // RESTRICT, never Cascade: FR-006 rules out hard-deleting a type at all, and a cascade that
        // could take booked classes down with it is the worst failure available here. Configured
        // without a navigation property on either side, deliberately - see Class.ClassTypeId.
        builder.HasOne<ClassType>()
            .WithMany()
            .HasForeignKey(x => x.ClassTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
