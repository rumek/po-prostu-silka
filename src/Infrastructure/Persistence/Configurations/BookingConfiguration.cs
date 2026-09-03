using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAt).IsRequired();

        // Null while the booking is active; set when the spot is released. Nullable is the whole
        // point - it is what distinguishes "never cancelled" from "cancelled at an unknown time".
        builder.Property(x => x.CancelledAt);

        // Stored as int, like ClassStatus. Defaulting to Active means a row inserted without an
        // explicit status holds its spot, never silently frees it.
        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(BookingStatus.Active);

        // 450 is Identity's key length, so the FK column matches AspNetUsers.Id exactly rather than
        // relying on a convention default - the same reasoning as Class.InstructorUserId.
        builder.Property(x => x.MemberUserId).IsRequired().HasMaxLength(450);

        // RESTRICT on both sides, following ClassConfiguration. A cascade here would be the worst
        // failure available: deleting a class or an account would silently erase the evidence that
        // someone had signed up, and the delete of a booked class is refused outright anyway.
        builder.HasOne(x => x.Class)
            .WithMany()
            .HasForeignKey(x => x.ClassId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // FILTERED, not plain - the same shape and the same reasoning as IX_ClassTypes_Name_Active.
        // A member may hold at most ONE active booking per class, but cancelling must not hold the
        // pair hostage: FR-009 keeps the cancelled row in history, and the member is allowed to book
        // the class again, so a plain unique index would reject the second booking forever.
        //
        // This is defence in depth rather than the primary mechanism. Class.ConcurrencyStamp already
        // serializes every booking write against a class, so two concurrent bookings by the same
        // member collide on the stamp and the retry sees the first one. This index is what still holds
        // if a future write path forgets to rotate the stamp.
        //
        // "[Status] = 0" names BookingStatus.Active as a literal - the enum's numeric values are
        // pinned for exactly this kind of dependency.
        builder.HasIndex(x => new { x.ClassId, x.MemberUserId })
            .IsUnique()
            .HasFilter("[Status] = 0")
            .HasDatabaseName("IX_Bookings_Class_Member_Active");

        // The member's upcoming-bookings query: MemberUserId equality, then Status equality. Also the
        // index the block cascade seeks on when it releases a blocked member's future spots.
        builder.HasIndex(x => new { x.MemberUserId, x.Status })
            .HasDatabaseName("IX_Bookings_Member_Status");
    }
}
