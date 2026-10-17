using System.Reflection;
using Uinsure.Policies.Architecture.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// A8: Domain entities change only through their own methods. An init accessor (records,
// positional parameters) can't mutate after construction, so it isn't a setter here.
public sealed class DomainEntityTests
{
    [Fact]
    public void Domain_types_have_no_public_setter_other_than_init()
    {
        const BindingFlags publicDeclared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var setters = SourceAssemblies.Domain.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Uinsure.Policies.Domain", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetProperties(publicDeclared))
            .Where(property => property.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter))
            .Select(property => $"{property.DeclaringType?.FullName}.{property.Name}");

        Assert.Empty(setters);
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
