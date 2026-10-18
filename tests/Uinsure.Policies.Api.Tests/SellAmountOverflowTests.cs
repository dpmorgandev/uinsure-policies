using System.Net;
using Microsoft.Extensions.Logging;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// OWASP API4:2023 (finding A1/I1): no amount a request can bind may throw on the way to a rule. An extreme
// negative amount with no fractional part binds under strict JSON, so it must be answered as an ordinary
// request: never a 500, never an Error-level log, and nothing stored. Run in Production, where a 500 is
// logged at Error by the unexpected-exception handler. API4 is not a requirements rule, so no Rule trait;
// the SELL-7 422 these amounts must get is asserted in SellAmountShapeTests.
public sealed class SellAmountOverflowTests : IClassFixture<LogCapturingApiFactory>
{
    private readonly LogCapturingApiFactory _factory;

    public SellAmountOverflowTests(LogCapturingApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Amount_of_minus_10_to_the_27_is_not_a_500_and_logs_no_error()
    {
        await AssertHandledWithoutError(-1_000_000_000_000_000_000_000_000_000m, "OVERFLOW-NEG-E27");
    }

    [Fact]
    public async Task Amount_of_decimal_MinValue_is_not_a_500_and_logs_no_error()
    {
        await AssertHandledWithoutError(decimal.MinValue, "OVERFLOW-NEG-MIN");
    }

    private async Task AssertHandledWithoutError(decimal amount, string reference)
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder(reference).WithAmount(amount).Build();
        var logged = _factory.Logs.Entries.Count;

        using var response = await client.PostPolicy(body);

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(_factory.Logs.Entries.Skip(logged), entry => entry.Level >= LogLevel.Error);
        await client.AssertNotStored(reference);
    }
}
