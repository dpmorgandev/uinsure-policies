using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class RefundConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> builder)
    {
        builder.ToTable("Refund");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Reference).HasMaxLength(40).IsRequired();
        builder.Property(r => r.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Amount).HasPrecision(18, 2);
        builder.Property(r => r.PaymentReference).HasMaxLength(40).IsRequired();
    }
}
