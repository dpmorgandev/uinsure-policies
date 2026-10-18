using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class PolicyConfiguration : IEntityTypeConfiguration<PolicyRecord>
{
    public void Configure(EntityTypeBuilder<PolicyRecord> builder)
    {
        builder.ToTable("Policies");
        builder.HasKey(p => p.Id);

        // SELL-1: the stored reference is already normalised, so a plain unique index is case-insensitive in effect.
        builder.Property(p => p.Reference).HasMaxLength(20).IsRequired();
        builder.HasIndex(p => p.Reference).IsUnique();

        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Version).IsConcurrencyToken();

        builder.HasMany(p => p.Policyholders).WithOne().HasForeignKey("PolicyId").IsRequired();
        builder.HasOne(p => p.Property).WithOne().HasForeignKey<PropertyRecord>("PolicyId").IsRequired();
        builder.HasMany(p => p.Terms).WithOne().HasForeignKey("PolicyId").IsRequired();

        // The aggregate is always loaded whole. The two collections are included, in order, by PolicyRepository.
        builder.Navigation(p => p.Property).AutoInclude();
    }
}
