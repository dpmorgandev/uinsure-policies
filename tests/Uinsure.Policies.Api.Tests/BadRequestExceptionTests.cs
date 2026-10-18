using System.Net;
using Microsoft.Extensions.Logging;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// Transport row of the contract: a BadHttpRequestException keeps its own 413, 408 or 400, with a fixed
// title and detail per status, never its message, and is logged at Warning, not Error.
// No Rule trait: transport hardening is not a numbered rule.
public sealed class BadRequestExceptionTests : IClassFixture<BadRequestExceptionApiFactory>
{
    private readonly BadRequestExceptionApiFactory _factory;

    public BadRequestExceptionTests(BadRequestExceptionApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(408)]
    [InlineData(413)]
    public async Task Bad_request_exception_keeps_its_own_status_as_a_problem_with_no_ruleId(int status)
    {
        using var response = await Get(BadRequestEndpointStartupFilter.PathFor(status));

        await ProblemAssert.TransportProblem(response, (HttpStatusCode)status, BadRequestEndpointStartupFilter.PathFor(status));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(408)]
    [InlineData(413)]
    public async Task Bad_request_exception_message_and_byte_limit_never_reach_the_body(int status)
    {
        using var response = await Get(BadRequestEndpointStartupFilter.PathFor(status));

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(BadRequestEndpointStartupFilter.Sentinel, text, StringComparison.Ordinal);
        Assert.DoesNotContain("65536", text, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Title_and_detail_are_fixed_for_the_status_whatever_the_exception_message()
    {
        using var first = await Get(BadRequestEndpointStartupFilter.PathFor(413));
        using var second = await Get(BadRequestEndpointStartupFilter.OtherMessage413Path);

        var firstProblem = await ProblemAssert.TransportProblem(first, HttpStatusCode.RequestEntityTooLarge, BadRequestEndpointStartupFilter.PathFor(413));
        var secondProblem = await ProblemAssert.TransportProblem(second, HttpStatusCode.RequestEntityTooLarge, BadRequestEndpointStartupFilter.OtherMessage413Path);
        Assert.Equal(firstProblem.GetProperty("title").GetString(), secondProblem.GetProperty("title").GetString());
        Assert.Equal(firstProblem.GetProperty("detail").GetString(), secondProblem.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(400)]
    [InlineData(408)]
    [InlineData(413)]
    public async Task Bad_request_exception_is_logged_at_Warning_and_never_at_Error(int status)
    {
        var path = BadRequestEndpointStartupFilter.PathFor(status);
        var message = BadRequestEndpointStartupFilter.MessageFor(status);

        using var response = await Get(path);

        var forThisRequest = _factory.Logs.Entries
            .Where(entry => entry.Exception?.Message == message || entry.Message.Contains(path, StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(forThisRequest, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(forThisRequest, entry => entry.Level >= LogLevel.Error);
    }

    private async Task<HttpResponseMessage> Get(string path)
    {
        using var client = _factory.CreateClient();
        return await client.GetAsync(path, TestContext.Current.CancellationToken);
    }
}
