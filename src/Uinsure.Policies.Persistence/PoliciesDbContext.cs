using Microsoft.EntityFrameworkCore;

namespace Uinsure.Policies.Persistence;

public sealed class PoliciesDbContext(DbContextOptions<PoliciesDbContext> options) : DbContext(options)
{
}
