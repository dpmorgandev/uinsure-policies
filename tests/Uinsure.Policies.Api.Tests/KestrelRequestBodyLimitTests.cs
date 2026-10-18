using System.Net;
using System.Text;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The same limit end to end, on real Kestrel: one byte over is a 413 problem that doesn't name the limit.
public sealed class KestrelRequestBodyLimitTests : IClassFixture<KestrelApiFactory>
{
    private const int Limit = 65_536;

    private readonly KestrelApiFactory _factory;

    public KestrelRequestBodyLimitTests(KestrelApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public async Task Body_one_byte_over_65536_is_413_with_a_fixed_problem_that_does_not_name_the_limit()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostPolicy(PaddedSale("BODY-OVER-1", Limit + 1));

        await ProblemAssert.TransportProblem(response, HttpStatusCode.RequestEntityTooLarge, "/policies");
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("65536", text, StringComparison.Ordinal);
        Assert.DoesNotContain("65,536", text, StringComparison.Ordinal);
    }

    // The boundary: exactly the limit is accepted, and one byte more is not.
    [Fact]
    public async Task Body_of_exactly_65536_bytes_is_accepted_while_one_byte_more_is_413()
    {
        using var client = _factory.CreateClient();

        using var atLimit = await client.PostPolicy(PaddedSale("BODY-AT-1", Limit));
        using var overLimit = await client.PostPolicy(PaddedSale("BODY-AT-2", Limit + 1));

        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, overLimit.StatusCode);
    }

    // A valid sale, padded with trailing JSON whitespace to exactly the given number of UTF-8 bytes.
    private static string PaddedSale(string reference, int bytes)
    {
        var json = new SellPolicyRequestBuilder(reference).Build().ToJsonString();
        var padding = bytes - Encoding.UTF8.GetByteCount(json);
        Assert.True(padding >= 0, "The sale body is already over the size asked for.");
        return json + new string(' ', padding);
    }
}
