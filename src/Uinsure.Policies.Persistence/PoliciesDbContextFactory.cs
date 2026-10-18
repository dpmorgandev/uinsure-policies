using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Uinsure.Policies.Persistence;

// Design-time only: `dotnet ef` uses this to make SQL Server migrations. The connection is never opened.
internal sealed class PoliciesDbContextFactory : IDesignTimeDbContextFactory<PoliciesDbContext>
{
    public PoliciesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PoliciesDbContext>()
            .UseSqlServer("Server=(local);Database=Policies;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new PoliciesDbContext(options);
    }
}
