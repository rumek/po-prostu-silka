using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain.Notifications;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("PushSubscriptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId).IsRequired();

        // Push endpoints are long URLs - FCM and Mozilla both run well past 256 characters.
        builder.Property(x => x.Endpoint).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.P256dh).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Auth).IsRequired().HasMaxLength(100);
        builder.Property(x => x.CreatedAt).IsRequired();

        // Unique: the browser re-issues the same endpoint on re-subscribe, so this is what makes
        // subscribe an upsert instead of a duplicate factory.
        builder.HasIndex(x => x.Endpoint).IsUnique();

        // The worker fans out per member, so this is the lookup path.
        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
