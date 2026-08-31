using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuration for <see cref="SchemaMarker"/>. Exists mainly to establish the
/// convention: one IEntityTypeConfiguration class per entity, in this folder,
/// discovered automatically by AppDbContext.OnModelCreating.
/// </summary>
public class SchemaMarkerConfiguration : IEntityTypeConfiguration<SchemaMarker>
{
    public void Configure(EntityTypeBuilder<SchemaMarker> builder)
    {
        builder.ToTable("SchemaMarkers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AppliedAt)
            .IsRequired();
    }
}
