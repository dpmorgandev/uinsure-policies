using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services;

namespace Uinsure.Policies.Persistence;

internal sealed class PolicyRepository(PoliciesDbContext context) : IPolicyRepository
{
    private const int SqliteConstraintUnique = 2067;
    private const int SqlServerUniqueIndex = 2601;
    private const int SqlServerUniqueConstraint = 2627;

    public Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct) =>
        context.Policies.FirstOrDefaultAsync(p => p.Reference == reference, ct);

    public void Add(Policy policy) => context.Policies.Add(policy);

    public async Task SaveChanges(CancellationToken ct)
    {
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PersistenceConflictException(ConflictReason.Concurrency);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // SELL-1: two sales of one reference raced past the service's GetByReference check.
            throw new PersistenceConflictException(ConflictReason.DuplicateReference);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode == SqliteConstraintUnique,
        SqlException sqlServer => sqlServer.Number is SqlServerUniqueIndex or SqlServerUniqueConstraint,
        _ => false,
    };
}
