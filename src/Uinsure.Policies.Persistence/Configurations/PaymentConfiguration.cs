using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.ToTable("Payment");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Reference).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
    }
}
