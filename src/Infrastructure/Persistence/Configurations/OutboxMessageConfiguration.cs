using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain.Notifications;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(x => x.Id);

        // Stored as int. NotificationChannel and OutboxStatus pin explicit values precisely so this
        // mapping stays stable.
        builder.Property(x => x.Channel).IsRequired().HasConversion<int>();
        builder.Property(x => x.Status).IsRequired().HasConversion<int>().HasDefaultValue(OutboxStatus.Pending);

        builder.Property(x => x.Recipient).IsRequired().HasMaxLength(320);
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(200);

        // Unbounded: a rendered email body has no useful ceiling, and this is the column that makes
        // retention matter against the 2GB Basic cap.
        builder.Property(x => x.Body).IsRequired();

        builder.Property(x => x.AttemptCount).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.NextAttemptAt).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);

        // THE index that matters. This is the worker's claim predicate - the only query that runs
        // every polling interval, forever, on a 5-DTU tier. Without it that becomes a table scan
        // over a table designed to grow.
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt })
            .HasDatabaseName("IX_OutboxMessages_Status_NextAttemptAt");
    }
}
