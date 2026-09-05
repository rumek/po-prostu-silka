using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

public class TrainingPlanConfiguration : IEntityTypeConfiguration<TrainingPlan>
{
    public void Configure(EntityTypeBuilder<TrainingPlan> builder)
    {
        builder.ToTable("TrainingPlans");
        builder.HasKey(x => x.Id);

        // 120 is shorter than ClassType.Name's 200 on purpose: this is a heading on a phone, not a
        // catalogue entry. The endpoint's MaxNameLength and the Angular validator mirror it - all
        // three must stay in step, or an over-long value becomes a 500 at the database instead of a
        // 400 on the field.
        builder.Property(x => x.Name).IsRequired().HasMaxLength(120);

        // 450 is Identity's key length, so both FK columns match AspNetUsers.Id exactly rather than
        // relying on a convention default - the same reasoning as Booking.MemberUserId.
        builder.Property(x => x.MemberUserId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.AssignedByUserId).IsRequired().HasMaxLength(450);

        // Stored as int, like BookingStatus. Defaulting to Active means a row inserted without an
        // explicit status is the member's current plan, never silently archived on arrival.
        builder
            .Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(TrainingPlanStatus.Active);

        builder.Property(x => x.CreatedAt).IsRequired();

        // Null while the plan is current; set when a later assignment supersedes it.
        builder.Property(x => x.ArchivedAt);

        builder.Property(x => x.ConcurrencyStamp).IsRequired().HasMaxLength(36).IsConcurrencyToken();

        // RESTRICT ON BOTH, and not merely by preference. Two foreign keys from one table to
        // AspNetUsers is exactly the shape SQL Server refuses when either of them cascades - it
        // reports a multiple-cascade-path error and the migration will not apply. Restrict is also
        // what we would want anyway: deleting an account must never silently erase the plans written
        // for it or by it.
        builder
            .HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(x => x.AssignedBy)
            .WithMany()
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // FILTERED, not plain - the same shape and reasoning as IX_Bookings_Class_Member_Active. A
        // member may hold at most ONE active plan, but archived plans must not hold the member
        // hostage: every replacement leaves another archived row behind, and a plain unique index
        // would reject the second assignment forever.
        //
        // THIS INDEX IS THE PRIMARY MECHANISM, not defence in depth - measured, not assumed. Six
        // racers assigning at once still leave exactly one active plan with the stamp rotation
        // commented out of the assignment handler; weaken this index instead and all six succeed.
        // Assignment always INSERTS a new active row, so the collision surfaces here, on the
        // database, before TrainingPlan.ConcurrencyStamp gets a say. The stamp is the second line:
        // it fails a loser earlier and more cheaply, and it is the ONLY guard on the edit path,
        // which inserts nothing and so never touches this index.
        //
        // "[Status] = 0" names TrainingPlanStatus.Active as a literal - the enum's numeric values are
        // pinned for exactly this dependency.
        builder
            .HasIndex(x => x.MemberUserId)
            .IsUnique()
            .HasFilter("[Status] = 0")
            .HasDatabaseName("IX_TrainingPlans_Member_Active");
    }
}
