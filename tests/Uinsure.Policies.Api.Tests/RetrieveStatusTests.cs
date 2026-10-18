using System.Net;
using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// GET-1 status: derived from UK today on each request, never stored. Pending before the first start, Active
// from the start to the end inclusive, Expired after the end (Cancelled is F3's). Each side of each boundary
// is its own test: the policy is sold on TestClock.Today, then the clock moves to the day under test and the
// policy is read back whole. A policy sold to start 2026-11-08 ends on 2027-11-07; one sold to start today
// (2026-10-17) ends on 2027-10-16, which is in BST, so 23:30 UTC that day is already 17 October in the UK.
public sealed class RetrieveStatusTests : IClassFixture<PoliciesApiFactory>
{
    private static readonly DateOnly FutureStart = new(2026, 11, 8);
    private static readonly DateOnly FutureStartEnd = new(2027, 11, 7);
    private static readonly DateOnly TodayStartEnd = new(2027, 10, 16);

    private readonly PoliciesApiFactory _factory;

    public RetrieveStatusTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Policy_sold_to_start_in_the_future_reads_back_Pending()
    {
        using var client = _factory.CreateClient();
        var sold = new SellPolicyRequestBuilder("GET1-PENDING").WithStartDate(FutureStart).Build();

        var policy = await client.SellAndReadBack(sold, "GET1-PENDING");

        PolicyResponseAssert.ReadsBackAsSold(policy, sold, "GET1-PENDING", FutureStartEnd, "Pending");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Policy_sold_to_start_today_reads_back_Active()
    {
        using var client = _factory.CreateClient();
        var sold = new SellPolicyRequestBuilder("GET1-ACTIVE-TODAY").WithStartDate(TestClock.Today).Build();

        var policy = await client.SellAndReadBack(sold, "GET1-ACTIVE-TODAY");

        PolicyResponseAssert.ReadsBackAsSold(policy, sold, "GET1-ACTIVE-TODAY", TodayStartEnd, "Active");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Status_on_the_day_before_the_start_is_Pending()
    {
        await AssertStatusOn("GET1-START-MINUS-1", FutureStart, FutureStartEnd, new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), "Pending");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Status_on_the_start_date_is_Active()
    {
        await AssertStatusOn("GET1-START-DAY", FutureStart, FutureStartEnd, new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero), "Active");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Status_on_the_end_date_is_Active()
    {
        await AssertStatusOn("GET1-END-DAY", FutureStart, FutureStartEnd, new DateTimeOffset(2027, 11, 7, 12, 0, 0, TimeSpan.Zero), "Active");
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Status_on_the_day_after_the_end_date_is_Expired()
    {
        await AssertStatusOn("GET1-END-PLUS-1", FutureStart, FutureStartEnd, new DateTimeOffset(2027, 11, 8, 12, 0, 0, TimeSpan.Zero), "Expired");
    }

    // GMT control: 2027-11-07 is after the clocks go back, so 23:30 UTC is still 7 November in the UK.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Status_at_23_30_UTC_on_an_end_date_in_GMT_is_Active()
    {
        await AssertStatusOn("GET1-END-GMT", FutureStart, FutureStartEnd, new DateTimeOffset(2027, 11, 7, 23, 30, 0, TimeSpan.Zero), "Active");
    }

    // BST: at 23:30 UTC on 2027-10-16 the UK date is already 2027-10-17, the day after the end.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Status_at_23_30_UTC_on_an_end_date_in_BST_is_Expired_as_the_UK_date_is_the_next_day()
    {
        await AssertStatusOn("GET1-END-BST", TestClock.Today, TodayStartEnd, new DateTimeOffset(2027, 10, 16, 23, 30, 0, TimeSpan.Zero), "Expired");
    }

    // BST control: at 22:59 UTC on 2027-10-16 it is 23:59 in the UK, still the end date.
    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Status_at_22_59_UTC_on_an_end_date_in_BST_is_Active()
    {
        await AssertStatusOn("GET1-END-BST-CTRL", TestClock.Today, TodayStartEnd, new DateTimeOffset(2027, 10, 16, 22, 59, 0, TimeSpan.Zero), "Active");
    }

    // Sold on TestClock.Today, read back whole at the instant under test: the one term is the current term
    // whatever the status, because a policy outside every term shows its latest.
    private async Task AssertStatusOn(string reference, DateOnly start, DateOnly expectedEnd, DateTimeOffset readAt, string expectedStatus)
    {
        using var client = _factory.CreateClient();
        var sold = new SellPolicyRequestBuilder(reference).WithStartDate(start).Build();
        await client.SellAndReadBack(sold, reference);

        _factory.Clock.SetUtcNow(readAt);
        using var read = await client.GetPolicy(reference);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        PolicyResponseAssert.ReadsBackAsSold(await read.ReadJson(), sold, reference, expectedEnd, expectedStatus);
    }
}
