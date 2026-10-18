using System.Reflection;
using NetArchTest.Rules;
using Uinsure.Policies.Architecture.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// A1–A6: the References table of the architecture contract.
public sealed class LayeringTests
{
    [Fact]
    public void Domain_types_depend_on_no_Microsoft_namespace_and_not_on_TimeProvider()
    {
        var result = Types.InAssembly(SourceAssemblies.Domain)
            .Should().NotHaveDependencyOnAny("Microsoft", "System.TimeProvider")
            .GetResult();

        Assert.Empty(ArchResults.FailingTypeNamesIn(result, "Uinsure.Policies.Domain"));
    }

    [Fact]
    public void Domain_references_only_BCL_assemblies()
    {
        var nonBcl = SourceAssemblies.ReferencedAssemblyNames(SourceAssemblies.Domain)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard");

        Assert.Empty(nonBcl);
    }

    [Fact]
    public void Services_does_not_depend_on_EF_Core_AspNetCore_Persistence_or_Api()
    {
        AssertNoDependency(
            SourceAssemblies.Services,
            "Uinsure.Policies.Services",
            ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Uinsure.Policies.Persistence", "Uinsure.Policies.Api"]);
    }

    [Fact]
    public void Persistence_does_not_depend_on_AspNetCore_or_Api()
    {
        AssertNoDependency(
            SourceAssemblies.Persistence,
            "Uinsure.Policies.Persistence",
            ["Microsoft.AspNetCore", "Uinsure.Policies.Api"]);
    }

    [Fact]
    public void No_src_assembly_references_a_test_assembly()
    {
        var offending = SourceAssemblies.All
            .SelectMany(assembly => SourceAssemblies.ReferencedAssemblyNames(assembly)
                .Where(IsTestAssembly)
                .Select(reference => $"{assembly.GetName().Name} references {reference}"));

        Assert.Empty(offending);
    }

    [Fact]
    public void Api_types_other_than_Program_do_not_use_Persistence_or_EF_Core()
    {
        var result = Types.InAssembly(SourceAssemblies.Api)
            .Should().NotHaveDependencyOnAny("Uinsure.Policies.Persistence", "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.DoesNotContain(ArchResults.FailingTypeNames(result), name => !IsProgramOrNestedInIt(name));
    }

    [Fact]
    public void Api_contracts_do_not_use_AspNetCore()
    {
        var result = Types.InAssembly(SourceAssemblies.Api)
            .Should().NotHaveDependencyOnAny("Microsoft.AspNetCore")
            .GetResult();

        Assert.Empty(ArchResults.FailingTypeNamesIn(result, "Uinsure.Policies.Api.Contracts"));
    }

    private static void AssertNoDependency(Assembly assembly, string ownNamespace, string[] forbidden)
    {
        var typeLevel = Types.InAssembly(assembly)
            .Should().NotHaveDependencyOnAny(forbidden)
            .GetResult();
        var assemblyLevel = SourceAssemblies.ReferencedAssemblyNames(assembly)
            .Where(name => forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

        string[] failures = [.. ArchResults.FailingTypeNamesIn(typeLevel, ownNamespace), .. assemblyLevel];
        Assert.Empty(failures);
    }

    private static bool IsTestAssembly(string name) =>
        name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft.Extensions.TimeProvider.Testing", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.AspNetCore.Mvc.Testing", StringComparison.Ordinal)
        || name.EndsWith(".Tests", StringComparison.Ordinal);

    // Program is the composition root, the one Api type allowed to touch Persistence; its
    // compiler-generated nested types (lambdas, closures) come with it.
    private static bool IsProgramOrNestedInIt(string fullName)
    {
        var outer = fullName.Split('+', '/')[0];
        return outer == "Program" || outer.EndsWith(".Program", StringComparison.Ordinal);
    }
}
