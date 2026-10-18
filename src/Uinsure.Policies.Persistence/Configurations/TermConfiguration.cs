using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class TermConfiguration : IEntityTypeConfiguration<Term>
{
    public void Configure(EntityTypeBuilder<Term> builder)
    {
        builder.Property<int>("Id");
        builder.HasKey("Id");
        builder.Property(t => t.Start);
        builder.Property(t => t.End);
        builder.Property(t => t.Amount).HasPrecision(18, 2);

        // A term without a payment is unpaid, so it has no Payment row.
        builder.HasOne(t => t.Payment).WithOne().HasForeignKey<Payment>("TermId");
        builder.Navigation(t => t.Payment).AutoInclude();
    }
}
