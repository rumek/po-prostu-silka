using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuration for the custom columns on <see cref="ApplicationUser"/>. Identity's own keys,
/// indexes and table names are configured by IdentityDbContext's base OnModelCreating - do not
/// re-declare them here.
/// </summary>
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(100);

        // Stored as int. AccountStatus pins explicit values precisely so this mapping is stable.
        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(AccountStatus.Pending);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        // S-02's member list filters by status (FR-005). Non-unique by construction.
        builder.HasIndex(x => x.Status);
    }
}
