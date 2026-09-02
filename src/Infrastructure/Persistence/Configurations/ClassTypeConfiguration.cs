using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

public class ClassTypeConfiguration : IEntityTypeConfiguration<ClassType>
{
    public void Configure(EntityTypeBuilder<ClassType> builder)
    {
        builder.ToTable("ClassTypes");
        builder.HasKey(x => x.Id);

        // 200 matches Class.Name - the two are the same label seen from two sides.
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);

        // The one genuinely optional column in the scheduling context. 1000 is room for what a
        // class is, who it suits and what to bring, without becoming a page.
        builder.Property(x => x.Description).HasMaxLength(1000);

        builder.Property(x => x.DefaultDurationMinutes).IsRequired();
        builder.Property(x => x.DefaultCapacity).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Defaulting to true means a row inserted without an explicit flag is OFFERED, never
        // silently hidden - the same reasoning ClassConfiguration applies to Status.
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        // FILTERED, not plain. Uniqueness holds among ACTIVE types only (FR-006): without the
        // filter, deactivating a type would hold its name hostage forever, and the admin could
        // never re-create "Joga dla początkujących" after retiring one.
        //
        // This index is what actually closes the race the endpoint's pre-check only narrows - two
        // simultaneous creates both pass the check, and the second write fails here rather than
        // producing two active types with one name.
        builder.HasIndex(x => x.Name)
            .IsUnique()
            .HasFilter("[IsActive] = 1")
            .HasDatabaseName("IX_ClassTypes_Name_Active");
    }
}
