using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class ClaimConfiguration : IEntityTypeConfiguration<ClaimRecord>
{
    public void Configure(EntityTypeBuilder<ClaimRecord> builder)
    {
        builder.ToTable("Claim");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Description).HasMaxLength(500);
    }
}
