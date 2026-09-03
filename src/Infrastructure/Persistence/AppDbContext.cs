using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Notifications;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Infrastructure.Persistence;

/// <summary>
/// The application's EF Core context. Derives from <see cref="IdentityDbContext{TUser}"/>, so the
/// seven ASP.NET Core Identity tables are part of this model.
///
/// Entity configuration lives in <see cref="IEntityTypeConfiguration{TEntity}"/> classes discovered
/// by <c>ApplyConfigurationsFromAssembly</c> below - keep it that way. Later slices add their own
/// configuration classes and are picked up automatically, with no edit to OnModelCreating and no
/// fluent configuration accumulating in this file.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    public DbSet<Class> Classes => Set<Class>();

    public DbSet<ClassType> ClassTypes => Set<ClassType>();

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Must come FIRST: this is what builds the Identity model. Inverting the order with
        // ApplyConfigurationsFromAssembly drops it, and the Identity tables silently disappear.
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
