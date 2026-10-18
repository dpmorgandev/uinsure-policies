using NetArchTest.Rules;

namespace Uinsure.Policies.Architecture.Tests.Support;

internal static class ArchResults
{
    public static IReadOnlyList<string> FailingTypeNames(TestResult result) =>
        result.IsSuccessful ? [] : [.. (result.FailingTypes ?? []).Select(type => $"{type.FullName}")];

    // Failing types in a namespace or any namespace below it. Filtering here rather than with a
    // NetArchTest namespace predicate leaves out compiler-emitted types (e.g. Microsoft.CodeAnalysis.
    // EmbeddedAttribute) and doesn't depend on whether a predicate matches exactly or by prefix.
    public static IReadOnlyList<string> FailingTypeNamesIn(TestResult result, string ns) =>
        [.. FailingTypeNames(result).Where(name => name.StartsWith(ns + ".", StringComparison.Ordinal))];
}
