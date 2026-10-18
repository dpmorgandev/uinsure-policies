using System.Reflection;

namespace Uinsure.Policies.Architecture.Tests.Support;

// Loaded by name so the tests don't depend on any particular type existing in a layer.
internal static class SourceAssemblies
{
    public static Assembly Domain { get; } = Assembly.Load("Uinsure.Policies.Domain");

    public static Assembly Services { get; } = Assembly.Load("Uinsure.Policies.Services");

    public static Assembly Persistence { get; } = Assembly.Load("Uinsure.Policies.Persistence");

    public static Assembly Api { get; } = Assembly.Load("Uinsure.Policies.Api");

    public static IReadOnlyList<Assembly> All { get; } = [Domain, Services, Persistence, Api];

    public static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty);

    public static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)
        || type.Name.Contains('<');
}
