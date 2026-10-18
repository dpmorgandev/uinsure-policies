using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.Property<int>("Id");
        builder.HasKey("Id");
        builder.Property(p => p.AddressLine1).HasMaxLength(200).IsRequired();
        builder.Property(p => p.AddressLine2).HasMaxLength(200);
        builder.Property(p => p.AddressLine3).HasMaxLength(200);
        builder.Property(p => p.Postcode).HasMaxLength(8).IsRequired();
    }
}
