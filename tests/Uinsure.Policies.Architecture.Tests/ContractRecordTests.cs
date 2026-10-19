using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Uinsure.Policies.Architecture.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// MVC builds a positional record through its primary constructor and reads validation metadata from the
// constructor parameters. A ValidationAttribute on the property generated for one of those parameters
// ([property: Required]) makes MVC throw on every request that binds the record, so the request answers
// 500 instead of the 400 the attribute was meant to give. This holds at the stub step too, when the
// behaviour tests are all still red and cannot tell a canned body apart from a throwing route.
public sealed class ContractRecordTests
{
    private const string ContractsNamespace = "Uinsure.Policies.Api.Contracts";

    // The C# compiler emits <Clone>$ on every record class; MVC detects records the same way.
    private const string RecordCloneMethod = "<Clone>$";

    private static IEnumerable<Type> ContractRecords =>
        SourceAssemblies.Api.GetTypes()
            .Where(type => type.IsPublic && type.IsClass && type.Namespace == ContractsNamespace)
            .Where(type => type.GetMethod(RecordCloneMethod, BindingFlags.Public | BindingFlags.Instance) is not null);

    [Fact]
    [Trait("Rule", "COULD-2")]
    public void Contract_records_carry_no_validation_attribute_on_a_primary_constructor_property()
    {
        var records = ContractRecords.ToList();
        Assert.NotEmpty(records);

        var misplaced = records
            .SelectMany(record => record.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => record.GetProperty(parameter.Name!, BindingFlags.Public | BindingFlags.Instance))
                .OfType<PropertyInfo>()
                .Where(property => property.GetCustomAttributes<ValidationAttribute>(inherit: true).Any())
                .Select(property => $"{record.Name}.{property.Name}"))
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Empty(misplaced);
    }
}
