using System.Reflection;
using Uinsure.Policies.Architecture.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// A14: the Domain knows nothing of storage. No Domain type carries a Version (it lives on PolicyRecord), and no
// Domain class keeps a parameterless constructor for EF to materialise it (Persistence uses Policy.Restore).
public sealed class DomainPersistenceIgnoranceTests
{
    private const BindingFlags AnyDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static IEnumerable<Type> DomainTypes =>
        SourceAssemblies.Domain.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Uinsure.Policies.Domain", StringComparison.Ordinal) == true)
            .Where(type => !SourceAssemblies.IsCompilerGenerated(type));

    [Fact]
    public void No_Domain_type_has_a_Version_member()
    {
        var versions = DomainTypes
            .SelectMany(type => type.GetMembers(AnyDeclared))
            .Where(member => member.Name == "Version" || member.Name == "<Version>k__BackingField")
            .Select(member => $"{member.DeclaringType?.FullName}.{member.Name}");

        Assert.Empty(versions);
    }

    [Fact]
    public void No_Domain_class_has_a_parameterless_constructor()
    {
        var parameterless = DomainTypes
            .Where(type => type.IsClass && !type.IsAbstract)
            .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .Where(constructor => constructor.GetParameters().Length == 0)
            .Select(constructor => $"{constructor.DeclaringType?.FullName}()");

        Assert.Empty(parameterless);
    }
}
