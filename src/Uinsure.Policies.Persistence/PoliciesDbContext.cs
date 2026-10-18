using Microsoft.EntityFrameworkCore;
using Uinsure.Policies.Domain.Policies;

namespace Uinsure.Policies.Persistence;

public sealed class PoliciesDbContext(DbContextOptions<PoliciesDbContext> options) : DbContext(options)
{
    public DbSet<Policy> Policies => Set<Policy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PoliciesDbContext).Assembly);
}
