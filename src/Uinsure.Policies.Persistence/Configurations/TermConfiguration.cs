using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class TermConfiguration : IEntityTypeConfiguration<TermRecord>
{
    public void Configure(EntityTypeBuilder<TermRecord> builder)
    {
        builder.ToTable("Term");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Amount).HasPrecision(18, 2);

        // A term without a payment is unpaid, so it has no Payment row.
        builder.HasOne(t => t.Payment).WithOne().HasForeignKey<PaymentRecord>("TermId");
        builder.Navigation(t => t.Payment).AutoInclude();
    }
}
