using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using po_prostu_silka.Domain.Scheduling;

namespace po_prostu_silka.Infrastructure.Persistence.Configurations;

public class GroupRosterEntryConfiguration : IEntityTypeConfiguration<GroupRosterEntry>
{
    public void Configure(EntityTypeBuilder<GroupRosterEntry> builder)
    {
        builder.ToTable("GroupRosterEntries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AddedAt).IsRequired();

        // The Identity key length. No FK - see GroupRosterEntry.AddedBy.
        builder.Property(x => x.AddedBy).HasMaxLength(450);

        // RESTRICT on both sides, following BookingConfiguration: groups and members are never deleted
        // by the product (a group is deactivated, a member blocked), and a cascade would be the one
        // path that could erase a roster silently. TestDataSeeder's reset deletes these rows first.
        builder.HasOne(x => x.ClassGroup)
            .WithMany()
            .HasForeignKey(x => x.ClassGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Member)
            .WithMany()
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // One row per member per group. The add path checks first; this is what holds when two staff
        // add the same person at the same instant, and it surfaces as the unique violation AddToRoster
        // reads as already_in_roster. Group leads: every roster read filters on it.
        builder.HasIndex(x => new { x.ClassGroupId, x.MemberId })
            .IsUnique()
            .HasDatabaseName("IX_GroupRosterEntries_Group_Member");

        // The karnet hook's "which groups is this member in".
        builder.HasIndex(x => x.MemberId)
            .HasDatabaseName("IX_GroupRosterEntries_MemberId");
    }
}
