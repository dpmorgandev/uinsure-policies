using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<PropertyRecord>
{
    public void Configure(EntityTypeBuilder<PropertyRecord> builder)
    {
        builder.ToTable("Property");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.AddressLine1).HasMaxLength(200).IsRequired();
        builder.Property(p => p.AddressLine2).HasMaxLength(200);
        builder.Property(p => p.AddressLine3).HasMaxLength(200);
        builder.Property(p => p.Postcode).HasMaxLength(8).IsRequired();
    }
}
