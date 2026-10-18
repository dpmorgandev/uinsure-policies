using Uinsure.Policies.Architecture.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// A9: no Unit of Work and no generic repository. A10: the repository is internal sealed.
public sealed class RepositoryTests
{
    private static IEnumerable<Type> SourceTypes =>
        SourceAssemblies.All.SelectMany(assembly => assembly.GetTypes())
            .Where(type => !SourceAssemblies.IsCompilerGenerated(type));

    [Fact]
    public void No_src_type_is_a_unit_of_work()
    {
        Assert.Empty(SourceTypes
            .Where(type => type.Name.Contains("UnitOfWork", StringComparison.OrdinalIgnoreCase))
            .Select(type => type.FullName));
    }

    [Fact]
    public void No_src_repository_type_is_generic()
    {
        Assert.Empty(SourceTypes
            .Where(type => type.IsGenericTypeDefinition && type.Name.Contains("Repository", StringComparison.Ordinal))
            .Select(type => type.FullName));
    }

    [Fact]
    public void Repository_classes_in_Persistence_are_internal_and_sealed()
    {
        var exposed = SourceAssemblies.Persistence.GetTypes()
            .Where(type => type.IsClass && !SourceAssemblies.IsCompilerGenerated(type))
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Where(type => type.IsPublic || type.IsNestedPublic || !type.IsSealed)
            .Select(type => type.FullName);

        Assert.Empty(exposed);
    }
}
