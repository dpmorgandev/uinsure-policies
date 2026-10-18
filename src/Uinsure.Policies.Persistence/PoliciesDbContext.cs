using Microsoft.EntityFrameworkCore;
using Uinsure.Policies.Persistence.Records;

namespace Uinsure.Policies.Persistence;

public sealed class PoliciesDbContext(DbContextOptions<PoliciesDbContext> options) : DbContext(options)
{
    internal DbSet<PolicyRecord> Policies => Set<PolicyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PoliciesDbContext).Assembly);
}
