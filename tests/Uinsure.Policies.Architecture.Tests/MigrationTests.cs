using Uinsure.Policies.Architecture.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// The initial SQL Server migration lives in Persistence and targets PoliciesDbContext. EF types are
// matched by name, so this project needs no compile-time reference to EF Core.
public sealed class MigrationTests
{
    private const string MigrationBaseName = "Microsoft.EntityFrameworkCore.Migrations.Migration";
    private const string DbContextAttributeName = "Microsoft.EntityFrameworkCore.Infrastructure.DbContextAttribute";
    private const string PoliciesDbContextName = "Uinsure.Policies.Persistence.PoliciesDbContext";

    [Fact]
    public void Persistence_has_a_migration_for_PoliciesDbContext()
    {
        var migrations = SourceAssemblies.Persistence.GetTypes()
            .Where(IsMigration)
            .Where(TargetsPoliciesDbContext);

        Assert.NotEmpty(migrations);
    }

    private static bool IsMigration(Type type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.FullName == MigrationBaseName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TargetsPoliciesDbContext(Type type) =>
        type.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.FullName == DbContextAttributeName
            && attribute.ConstructorArguments is [{ Value: Type context }]
            && context.FullName == PoliciesDbContextName);
}
