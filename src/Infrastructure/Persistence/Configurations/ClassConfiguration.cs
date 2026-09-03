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

        // Name, Room and Instructor are GONE, not merely unused. They were the occurrence's own
        // identity until S-06; the name and description now resolve through ClassType and the
        // instructor through ApplicationUser, and the club has one room so that field never carried
        // information at all.
        //
        // DROPPED IN THE SAME RELEASE THAT STOPPED WRITING THEM - a deliberate exception to
        // AGENTS.md's one-release lag, taken by the product owner. See DropDeadClassColumns for what
        // that costs on a rollback.
        builder.Property(x => x.StartsAt).IsRequired();
        builder.Property(x => x.DurationMinutes).IsRequired();
        builder.Property(x => x.Capacity).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // The no-overbooking guarantee's mechanism - read Class.ConcurrencyStamp before touching this.
        //
        // IsConcurrencyToken is what puts the column in the WHERE clause of every UPDATE against
        // Classes, so a booking that rotates the stamp fails rather than overwrites when another
        // request got there first. 36 is a GUID in its dashed string form; the column is never
        // compared to anything but itself, so the length is a storage decision and nothing more.
        builder.Property(x => x.ConcurrencyStamp)
            .IsRequired()
            .HasMaxLength(36)
            .IsConcurrencyToken();

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
