using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Persistence.Records;
using Uinsure.Policies.Services;

namespace Uinsure.Policies.Persistence;

internal sealed class PolicyRepository(PoliciesDbContext context) : IPolicyRepository
{
    private const int SqliteConstraintUnique = 2067;
    private const int SqlServerUniqueIndex = 2601;
    private const int SqlServerUniqueConstraint = 2627;

    private readonly PolicyRecordMapper _mapper = new();

    // The record behind each policy handed out in this request. The repository is scoped, so the map is too.
    private readonly Dictionary<Policy, PolicyRecord> _records = new(ReferenceEqualityComparer.Instance);

    // SELL-4/SELL-5: holders and terms load in the order they were sold, which is their generated key order.
    public async Task<Policy?> GetByReference(PolicyReference reference, CancellationToken ct)
    {
        var record = await context.Policies
            .Include(p => p.Policyholders.OrderBy(h => h.Id))
            .Include(p => p.Terms.OrderBy(t => t.Id))
            .Include(p => p.Claims.OrderBy(c => c.Id))
            .Include(p => p.Cancellation).ThenInclude(c => c!.Terms.OrderBy(t => t.Id))
            .Include(p => p.Cancellation).ThenInclude(c => c!.Refunds.OrderBy(r => r.Id))
            .FirstOrDefaultAsync(p => p.Reference == reference.Value, ct);
        if (record is null)
        {
            return null;
        }

        var policy = _mapper.Restore(record);
        _records[policy] = record;
        return policy;
    }

    public void Add(Policy policy)
    {
        var record = _mapper.ToRecord(policy);
        record.Version = Guid.NewGuid();
        context.Policies.Add(record);
        _records[policy] = record;
    }

    public async Task SaveChanges(CancellationToken ct)
    {
        UpdateTrackedRecords();
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

    // Domain → tracked record, then a new Version for every loaded record that now differs from the database.
    private void UpdateTrackedRecords()
    {
        var changed = new HashSet<PolicyRecord>();
        foreach (var (policy, record) in _records)
        {
            record.AutoRenew = policy.AutoRenew;
            record.HasClaims = policy.HasClaims;

            // Claims only ever grow, and a cancellation is written once.
            foreach (var claim in policy.Claims.Skip(record.Claims.Count))
            {
                record.Claims.Add(_mapper.ToRecord(claim));
                changed.Add(record);
            }

            if (policy.Cancellation is not null && record.Cancellation is null)
            {
                record.Cancellation = _mapper.ToRecord(policy.Cancellation);
                changed.Add(record);
            }
        }

        context.ChangeTracker.DetectChanges();
        foreach (var record in _records.Values.Where(r => changed.Contains(r) || context.Entry(r).State == EntityState.Modified))
        {
            record.Version = Guid.NewGuid();
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode == SqliteConstraintUnique,
        SqlException sqlServer => sqlServer.Number is SqlServerUniqueIndex or SqlServerUniqueConstraint,
        _ => false,
    };
}
