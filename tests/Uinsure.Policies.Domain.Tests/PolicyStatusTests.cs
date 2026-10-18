using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// Status (requirements, the row after GET-1) is derived from today and never stored: Pending before the
// first start, Expired after the last end, otherwise Active. Cancelled is not reachable until F3. A
// one-term policy sold to start 2026-11-01 (TestClock.Today + 15) is covered from 2026-11-01 to
// 2027-10-31 inclusive. Untraited until F2 adds the GET-1 traits.
public sealed class PolicyStatusTests
{
    private static readonly DateOnly Start = TestClock.Today.AddDays(15);

    [Fact]
    public void Status_on_the_sale_day_before_the_start_is_Pending()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        Assert.Equal(PolicyStatus.Pending, policy.StatusOn(TestClock.Today));
    }

    [Fact]
    public void Status_on_the_day_before_the_start_is_Pending()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        Assert.Equal(PolicyStatus.Pending, policy.StatusOn(new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public void Status_on_the_start_day_is_Active()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        Assert.Equal(PolicyStatus.Active, policy.StatusOn(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void Status_on_the_end_day_is_Active()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        Assert.Equal(PolicyStatus.Active, policy.StatusOn(new DateOnly(2027, 10, 31)));
    }

    [Fact]
    public void Status_on_the_day_after_the_end_is_Expired()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        Assert.Equal(PolicyStatus.Expired, policy.StatusOn(new DateOnly(2027, 11, 1)));
    }
}
