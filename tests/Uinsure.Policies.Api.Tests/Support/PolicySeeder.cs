using Microsoft.Extensions.DependencyInjection;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// Stores a policy through the app's own IPolicyRepository, in a scope of its own as a request would, so the
// test reads back over HTTP exactly what Persistence stored. Used only where no endpoint can make the state.
internal static class PolicySeeder
{
    public static async Task Seed(this PoliciesApiFactory factory, Policy policy)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPolicyRepository>();
        repository.Add(policy);
        await repository.SaveChanges(TestContext.Current.CancellationToken);
    }
}
