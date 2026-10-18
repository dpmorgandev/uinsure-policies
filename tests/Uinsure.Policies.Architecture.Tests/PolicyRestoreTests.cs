using System.Reflection;
using Uinsure.Policies.Architecture.Tests.Support;
using Uinsure.Policies.Domain.Policies;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// A13: Policy.Restore rebuilds a policy from stored state without running any rule, so only Persistence may
// call it. Every method body in the four src assemblies is read; the test projects are not src and may call it.
public sealed class PolicyRestoreTests
{
    [Fact]
    public void Policy_has_a_public_static_Restore_that_returns_a_Policy()
    {
        var restore = Restores();

        var method = Assert.Single(restore);
        Assert.Equal(typeof(Policy), method.ReturnType);
    }

    [Fact]
    public void Policy_Restore_is_called_from_Persistence_and_from_no_other_src_assembly()
    {
        var restore = Restores();
        Assert.NotEmpty(restore);

        var callers = SourceAssemblies.All
            .SelectMany(assembly => MethodCalls.In(assembly)
                .Where(call => restore.Contains(call.Callee))
                .Select(call => (Assembly: assembly, call.Caller)))
            .ToArray();

        Assert.Contains(callers, call => call.Assembly == SourceAssemblies.Persistence);
        Assert.Empty(callers
            .Where(call => call.Assembly != SourceAssemblies.Persistence)
            .Select(call => $"{call.Assembly.GetName().Name}: {call.Caller}"));
    }

    // The scan itself: a call made inside an async method (PolicyService.Sell's state machine calls Policy.Sell)
    // is found and counted against the assembly it was written in, so an empty caller list means something.
    [Fact]
    public void Call_scan_finds_a_call_made_inside_an_async_method()
    {
        var sell = typeof(Policy).GetMethod(nameof(Policy.Sell), BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(sell);

        Assert.Contains(MethodCalls.In(SourceAssemblies.Services), call => call.Callee.Equals(sell));
    }

    private static MethodInfo[] Restores() =>
        [.. typeof(Policy).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "Restore")];
}
