using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class CancellationConfiguration : IEntityTypeConfiguration<CancellationRecord>
{
    public void Configure(EntityTypeBuilder<CancellationRecord> builder)
    {
        builder.ToTable("Cancellation");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.RuleApplied).HasMaxLength(20).IsRequired();
        builder.Property(c => c.TotalRefund).HasPrecision(18, 2);

        builder.HasMany(c => c.Terms).WithOne().HasForeignKey("CancellationId").IsRequired();
        builder.HasMany(c => c.Refunds).WithOne().HasForeignKey("CancellationId").IsRequired();
    }
}
