using System.Net;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// In Development too, a BadHttpRequestException keeps its status and never shows its message, which names
// the byte limit. No Rule trait: transport hardening.
public sealed class DevelopmentBadRequestExceptionTests : IClassFixture<DevelopmentBadRequestExceptionApiFactory>
{
    private readonly DevelopmentBadRequestExceptionApiFactory _factory;

    public DevelopmentBadRequestExceptionTests(DevelopmentBadRequestExceptionApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task In_Development_a_413_bad_request_exception_keeps_its_status_and_still_hides_its_message()
    {
        var path = BadRequestEndpointStartupFilter.PathFor(413);
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        await ProblemAssert.TransportProblem(response, HttpStatusCode.RequestEntityTooLarge, path);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(BadRequestEndpointStartupFilter.Sentinel, text, StringComparison.Ordinal);
        Assert.DoesNotContain("65536", text, StringComparison.Ordinal);
    }
}
