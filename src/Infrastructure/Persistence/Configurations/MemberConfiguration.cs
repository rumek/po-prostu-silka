using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain.Members;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuration for <see cref="Member"/> — the club's own record of a person, which may or may not
/// have an Identity account behind it.
/// </summary>
public class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("Members");
        builder.HasKey(x => x.Id);

        // 450 matches AspNetUsers.Id exactly rather than relying on a convention default - the same
        // reasoning BookingConfiguration gave for the column this one replaces.
        builder.Property(x => x.UserId).HasMaxLength(450);

        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(100);

        // 256 is Identity's own email length, so a claimed member's address round-trips without loss.
        builder.Property(x => x.Email).HasMaxLength(256);

        // The SAME constants ApplicationUserConfiguration bounds the account's copies with. Sharing the
        // source is the point: while both rows carry contact details (they do until the read flips),
        // a length that disagreed between them would truncate on the way across.
        builder.Property(x => x.PhoneNumber).HasMaxLength(ContactDetails.PhoneNumberMaxLength);
        builder.Property(x => x.Street).HasMaxLength(ContactDetails.StreetMaxLength);
        builder.Property(x => x.HouseNumber).HasMaxLength(ContactDetails.HouseNumberMaxLength);
        builder.Property(x => x.PostalCode).HasMaxLength(ContactDetails.PostalCodeMaxLength);
        builder.Property(x => x.City).HasMaxLength(ContactDetails.CityMaxLength);

        // Stored as int. MembershipStatus pins explicit values precisely so this mapping is stable, and
        // so the filtered indexes below can name them as literals.
        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(MembershipStatus.Active);

        builder.Property(x => x.CreatedAt).IsRequired();

        // Long enough for the 8-character code plus the separator a future format might want; short
        // enough that the unique index below stays narrow.
        builder.Property(x => x.AccessCode).HasMaxLength(12);

        // 36 is a Guid's string form, matching TrainingPlanConfiguration's stamp.
        builder.Property(x => x.ConcurrencyStamp)
            .IsRequired()
            .HasMaxLength(36)
            .IsConcurrencyToken();

        // RESTRICT, like every other foreign key in this application except the push subscriptions.
        // Deleting an account must not silently take the club's record of the person with it - and
        // there is no account-deletion path anyway.
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // ONE ACCOUNT, ONE MEMBER - and this index is what enforces it, not convention.
        //
        // THE "IS NOT NULL" FILTER IS LOAD-BEARING, NOT DECORATION. SQL Server treats NULLs as EQUAL
        // for uniqueness, so a plain unique index here would accept the first member without an account
        // and reject every one after it - which is the exact case this whole slice exists to support.
        // The same trap applies to the access-code and email indexes below.
        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL")
            .HasDatabaseName("IX_Members_UserId");

        // A live code identifies exactly one member - that is what makes redeeming it unambiguous, and
        // what turns a generation collision into a retryable unique violation rather than a code that
        // silently claims the wrong record.
        builder.HasIndex(x => x.AccessCode)
            .IsUnique()
            .HasFilter("[AccessCode] IS NOT NULL")
            .HasDatabaseName("IX_Members_AccessCode");

        // Mirrors Identity's own uniqueness on account email, so a member recorded by the admin cannot
        // collide with an account that already exists - and so "find the person with this address" has
        // one answer.
        builder.HasIndex(x => x.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL")
            .HasDatabaseName("IX_Members_Email");

        // The admin list filters by membership status, exactly as it filters accounts by AccountStatus.
        // Non-unique by construction.
        builder.HasIndex(x => x.Status)
            .HasDatabaseName("IX_Members_Status");
    }
}
