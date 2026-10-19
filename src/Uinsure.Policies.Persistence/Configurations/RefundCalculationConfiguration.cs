using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class RefundCalculationConfiguration : IEntityTypeConfiguration<RefundCalculationRecord>
{
    public void Configure(EntityTypeBuilder<RefundCalculationRecord> builder)
    {
        builder.ToTable("RefundCalculation");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.Property(c => c.RuleApplied).HasMaxLength(20).IsRequired();
    }
}
