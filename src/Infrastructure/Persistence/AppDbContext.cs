using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;

namespace po_prostu_silka.Infrastructure.Persistence;

/// <summary>
/// The application's EF Core context.
///
/// Entity configuration lives in <see cref="IEntityTypeConfiguration{TEntity}"/> classes
/// discovered by <c>ApplyConfigurationsFromAssembly</c> below - keep it that way. Later
/// slices add their own configuration classes and are picked up automatically, with no
/// edit to OnModelCreating and no fluent configuration accumulating in this file.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SchemaMarker> SchemaMarkers => Set<SchemaMarker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
