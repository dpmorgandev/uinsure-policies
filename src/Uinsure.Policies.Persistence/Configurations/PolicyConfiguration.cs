using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        builder.Property<int>("Id");
        builder.HasKey("Id");

        // SELL-1: the stored reference is already normalised, so a plain unique index is case-insensitive in effect.
        builder.Property(p => p.Reference)
            .HasConversion(reference => reference.Value, value => new PolicyReference(value))
            .HasMaxLength(20)
            .IsRequired();
        builder.HasIndex(p => p.Reference).IsUnique();

        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.AutoRenew);
        builder.Property(p => p.HasClaims);
        builder.Property(p => p.Version).IsConcurrencyToken();

        builder.HasMany(p => p.Policyholders).WithOne().HasForeignKey("PolicyId").IsRequired();
        builder.HasOne(p => p.Property).WithOne().HasForeignKey<Property>("PolicyId").IsRequired();
        builder.HasMany(p => p.Terms).WithOne().HasForeignKey("PolicyId").IsRequired();

        // The aggregate is always loaded whole. The two collections are included, in order, by PolicyRepository.
        builder.Navigation(p => p.Property).AutoInclude();
    }
}
