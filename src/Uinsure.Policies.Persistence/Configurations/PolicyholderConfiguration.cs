using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Persistence.Configurations;

internal sealed class PolicyholderConfiguration : IEntityTypeConfiguration<Policyholder>
{
    public void Configure(EntityTypeBuilder<Policyholder> builder)
    {
        builder.Property<int>("Id");
        builder.HasKey("Id");
        builder.Property(h => h.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(h => h.LastName).HasMaxLength(100).IsRequired();
    }
}
