using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class PolicyholderConfiguration : IEntityTypeConfiguration<PolicyholderRecord>
{
    public void Configure(EntityTypeBuilder<PolicyholderRecord> builder)
    {
        builder.ToTable("Policyholder");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(h => h.LastName).HasMaxLength(100).IsRequired();
    }
}
