using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuration for <see cref="MembershipPass"/> — the karnet that decides whether a person may be
/// booked into a class (S-16).
/// </summary>
public class MembershipPassConfiguration : IEntityTypeConfiguration<MembershipPass>
{
    public void Configure(EntityTypeBuilder<MembershipPass> builder)
    {
        builder.ToTable("MembershipPasses");
        builder.HasKey(x => x.Id);

        // The width MembershipPassRules declares - one source, so the column, the endpoint's
        // validation and the Angular form cannot disagree about what fits.
        builder.Property(x => x.TypeName)
            .IsRequired()
            .HasMaxLength(MembershipPassRules.TypeNameMaxLength);

        // THE FIRST DateOnly MAPPING IN THIS MODEL, and therefore explicit rather than left to
        // convention. Every other temporal column here is DateTimeOffset; these two are days, not
        // instants, and "date" is what makes an inclusive range comparison mean what it reads like.
        // Without HasColumnType the provider would still choose "date" today, but a convention that
        // happens to be right is not the same as a decision - and this one is load-bearing enough to
        // state, since a datetime2 column would quietly make "valid to the 30th" exclude most of it.
        builder.Property(x => x.ValidFrom).IsRequired().HasColumnType("date");
        builder.Property(x => x.ValidTo).IsRequired().HasColumnType("date");

        // The number ISSUED. See MembershipPass for why there is no remaining-balance column beside it.
        builder.Property(x => x.EntryCount).IsRequired();

        builder.Property(x => x.IssuedAt).IsRequired();

        // 36 is a Guid's string form, matching MemberConfiguration's stamp.
        builder.Property(x => x.ConcurrencyStamp)
            .IsRequired()
            .HasMaxLength(36)
            .IsConcurrencyToken();

        // RESTRICT, like every other foreign key in this application except the push subscriptions.
        // Deleting a person must never silently take the record of what they were entitled to.
        builder.HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves both reads this entity has: the overlap probe (MemberId equality, then a range scan
        // on ValidFrom) and the admin's pass history for one member. NOT UNIQUE, and no filtered
        // variant either - non-overlap is a rule ABOUT PAIRS OF ROWS, and no index in SQL Server can
        // express "no two ranges for this member intersect". The check lives in the endpoint and is
        // made atomic by rotating Member.ConcurrencyStamp; this index only makes it cheap.
        builder.HasIndex(x => new { x.MemberId, x.ValidFrom })
            .HasDatabaseName("IX_MembershipPasses_MemberId_ValidFrom");
    }
}
