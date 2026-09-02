using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

public class ClassConfiguration : IEntityTypeConfiguration<Class>
{
    public void Configure(EntityTypeBuilder<Class> builder)
    {
        builder.ToTable("Classes");
        builder.HasKey(x => x.Id);

        // THE THREE DEAD COLUMNS (Name, Room, Instructor). Nothing reads or writes them since S-06 -
        // the name and description resolve through ClassType, the instructor through ApplicationUser,
        // and the club has one room so that field never carried information.
        //
        // They stay in the schema, nullable, for exactly ONE RELEASE. AGENTS.md: rollback redeploys
        // the previous artifact but does NOT roll back the database, so the previous build - which
        // still INSERTs all three, NOT NULL - has to find them. A follow-up change drops them.
        //
        // The lengths are kept as they were: a column that may come back into use on a rollback must
        // still accept what that build writes.
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Room).HasMaxLength(100);

        // The property is renamed, the column is not - Instructor is the navigation now, and the two
        // cannot share a name. See Class.InstructorName.
        builder.Property(x => x.InstructorName).HasColumnName("Instructor").HasMaxLength(100);

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

        // The time-overlap check (prd-v2 FR-012), which runs once per create/edit and once per
        // duplicated week. It REPLACES IX_Classes_Room_StartsAt: the rule widened from "one room, one
        // class at a time" to "one club, one class at a time", so the equality half of that index is
        // gone and what remains is a pure range scan over StartsAt.
        builder.HasIndex(x => x.StartsAt)
            .HasDatabaseName("IX_Classes_StartsAt");

        // The definition this occurrence instantiates (prd-v2 FR-008). REQUIRED since S-06.
        //
        // RESTRICT, never Cascade: FR-006 rules out hard-deleting a type at all, and a cascade that
        // could take booked classes down with it is the worst failure available here.
        //
        // The navigation is a READ-SIDE affordance and nothing more - see Class.ClassType before
        // using it. No write path may reach DefaultCapacity through it.
        builder.Property(x => x.ClassTypeId).IsRequired();

        builder.HasOne(x => x.ClassType)
            .WithMany()
            .HasForeignKey(x => x.ClassTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Who runs it (prd-v2 FR-009). 450 is Identity's key length, so the FK column matches
        // AspNetUsers.Id exactly rather than relying on a convention default.
        //
        // RESTRICT for the same reason as above, plus one of its own: a trainer's account must never
        // be deletable out from under a scheduled class.
        builder.Property(x => x.InstructorUserId).IsRequired().HasMaxLength(450);

        builder.HasOne(x => x.Instructor)
            .WithMany()
            .HasForeignKey(x => x.InstructorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.InstructorUserId)
            .HasDatabaseName("IX_Classes_InstructorUserId");
    }
}
