using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// SELL-3: the client sends only the start date; the end date is start + 1 year − 1 day, cover inclusive.
// A 29 Feb start ends on 28 Feb of the next year.
public sealed class SellTermLengthTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public SellTermLengthTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "SELL-3")]
    public async Task End_date_is_start_plus_one_year_minus_one_day()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL3-ONE-YEAR").WithStartDate(new DateOnly(2026, 11, 1)).Build();

        var policy = await client.SellAndReadBack(body, "SELL3-ONE-YEAR");

        Assert.Equal(new DateOnly(2026, 11, 1), policy.GetDate("startDate"));
        Assert.Equal(new DateOnly(2027, 10, 31), policy.GetDate("endDate"));
    }

    [Fact]
    [Trait("Rule", "SELL-3")]
    public async Task Sale_creates_one_term_numbered_1_with_the_calculated_end_date()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL3-TERM").WithStartDate(new DateOnly(2026, 11, 1)).WithAmount(365.00m).Build();

        var policy = await client.SellAndReadBack(body, "SELL3-TERM");

        var term = Assert.Single(policy.GetProperty("terms").EnumerateArray());
        Assert.Equal(1, term.GetProperty("termNumber").GetInt32());
        Assert.Equal(new DateOnly(2026, 11, 1), term.GetDate("startDate"));
        Assert.Equal(new DateOnly(2027, 10, 31), term.GetDate("endDate"));
        Assert.Equal(365.00m, term.GetProperty("amount").GetDecimal());
    }

    // The term crosses 29 Feb 2028, so it is 366 days long and ends on 29 Feb.
    [Fact]
    [Trait("Rule", "SELL-3")]
    public async Task Start_on_1_March_2027_ends_on_29_February_2028()
    {
        _factory.Clock.SetUtcNow(new DateTimeOffset(2027, 1, 15, 12, 0, 0, TimeSpan.Zero));
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL3-1-MAR-2027").WithStartDate(new DateOnly(2027, 3, 1)).Build();

        var policy = await client.SellAndReadBack(body, "SELL3-1-MAR-2027");

        Assert.Equal(new DateOnly(2028, 2, 29), policy.GetDate("endDate"));
    }

    // The named 29 Feb test: 29 Feb 2028 + 1 year has no 29 Feb, and the term ends on 28 Feb 2029.
    [Fact]
    [Trait("Rule", "SELL-3")]
    public async Task Start_on_29_February_2028_ends_on_28_February_2029()
    {
        _factory.Clock.SetUtcNow(new DateTimeOffset(2028, 1, 15, 12, 0, 0, TimeSpan.Zero));
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL3-29-FEB-2028").WithStartDate(new DateOnly(2028, 2, 29)).Build();

        var policy = await client.SellAndReadBack(body, "SELL3-29-FEB-2028");

        Assert.Equal(new DateOnly(2028, 2, 29), policy.GetDate("startDate"));
        Assert.Equal(new DateOnly(2029, 2, 28), policy.GetDate("endDate"));
    }

    [Fact]
    [Trait("Rule", "SELL-3")]
    public async Task Request_with_an_end_date_is_rejected_with_400_as_an_unknown_field()
    {
        using var client = _factory.CreateClient();
        var body = new SellPolicyRequestBuilder("SELL3-END-SENT").Build();
        body["endDate"] = "2027-10-31";

        using var response = await client.PostPolicy(body);

        ProblemAssert.HasError(await ProblemAssert.ValidationErrors(response), "$.endDate");
        await client.AssertNotStored("SELL3-END-SENT");
    }
}
