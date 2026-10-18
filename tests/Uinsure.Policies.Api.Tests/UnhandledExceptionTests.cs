using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The 500 row of the contract, outside Development: a generic RFC 9457 body with a traceId, the
// exception's details in the logs only.
public sealed class UnhandledExceptionTests : IClassFixture<UnhandledExceptionApiFactory>
{
    private readonly UnhandledExceptionApiFactory _factory;

    public UnhandledExceptionTests(UnhandledExceptionApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Unhandled_exception_returns_500_problem_details_with_a_generic_detail_and_a_traceId()
    {
        using var response = await GetThrowingPath();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var body = problem.RootElement;
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.6.1", body.GetProperty("type").GetString());
        Assert.Equal("An error occurred while processing your request.", body.GetProperty("title").GetString());
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
        Assert.Equal(ThrowingEndpointStartupFilter.Path, body.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
        Assert.False(body.TryGetProperty("ruleId", out _));
    }

    [Fact]
    public async Task Unhandled_exception_response_reveals_no_exception_message_type_or_stack_trace_outside_Development()
    {
        using var response = await GetThrowingPath();

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(ThrowingEndpointStartupFilter.Sentinel, text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ThrowingEndpointStartupFilter), text, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unhandled_exception_is_logged_with_the_exception()
    {
        using var response = await GetThrowingPath();

        Assert.Contains(_factory.Logs.Entries, entry =>
            entry.Level >= LogLevel.Error && entry.Exception?.Message == ThrowingEndpointStartupFilter.Sentinel);
    }

    private async Task<HttpResponseMessage> GetThrowingPath()
    {
        using var client = _factory.CreateClient();
        return await client.GetAsync(ThrowingEndpointStartupFilter.Path, TestContext.Current.CancellationToken);
    }
}
