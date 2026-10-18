using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Uinsure.Policies.Architecture.Tests.Support;
using Uinsure.Policies.Persistence;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// A12: EF maps only the Persistence records, internal *Record types in Uinsure.Policies.Persistence.Records,
// never a Domain type; the configurations configure records; and the concurrency token is PolicyRecord.Version.
// The model is PoliciesDbContext's own, built from its configurations; building it opens no connection.
public sealed class PersistenceRecordTests
{
    private const string RecordsNamespace = "Uinsure.Policies.Persistence.Records";
    private const string EntityTypeConfigurationName = "Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1";

    [Fact]
    public void EF_maps_only_Persistence_record_types()
    {
        var notRecords = MappedTypes().Where(type => !IsRecordType(type)).Select(type => type.FullName);

        Assert.Empty(notRecords);
    }

    [Fact]
    public void Persistence_record_types_exist_and_are_internal()
    {
        var records = SourceAssemblies.Persistence.GetTypes()
            .Where(type => type.Namespace == RecordsNamespace && !SourceAssemblies.IsCompilerGenerated(type))
            .ToArray();

        Assert.Contains(records, type => type.Name == "PolicyRecord");
        Assert.Empty(records.Where(type => type.IsPublic || type.IsNestedPublic).Select(type => type.FullName));
    }

    [Fact]
    public void Entity_type_configurations_configure_only_record_types()
    {
        var configured = SourceAssemblies.Persistence.GetTypes()
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition().FullName == EntityTypeConfigurationName)
            .Select(contract => contract.GetGenericArguments()[0])
            .ToArray();

        Assert.NotEmpty(configured);
        Assert.Empty(configured.Where(type => !IsRecordType(type)).Select(type => type.FullName));
    }

    [Fact]
    public void PolicyRecord_Version_is_the_concurrency_token()
    {
        using var context = NewContext();
        var policyRecord = context.Model.GetEntityTypes()
            .SingleOrDefault(entity => entity.ClrType.FullName == $"{RecordsNamespace}.PolicyRecord");
        Assert.NotNull(policyRecord);

        var tokens = policyRecord.GetProperties().Where(property => property.IsConcurrencyToken).Select(property => property.Name);
        Assert.Equal(["Version"], tokens);
    }

    private static bool IsRecordType(Type type) =>
        type.Assembly == SourceAssemblies.Persistence
        && type.Namespace == RecordsNamespace
        && type.Name.EndsWith("Record", StringComparison.Ordinal)
        && !type.IsPublic
        && !type.IsNestedPublic;

    private static Type[] MappedTypes()
    {
        using var context = NewContext();
        IModel model = context.Model;
        var types = model.GetEntityTypes().Select(entity => entity.ClrType).Distinct().ToArray();
        Assert.NotEmpty(types);
        return types;
    }

    private static PoliciesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PoliciesDbContext>().UseSqlite("DataSource=:memory:").Options);
}
