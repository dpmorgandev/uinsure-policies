using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Domain.Tests.Builders;
using Uinsure.Policies.Domain.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// Status (requirements, the row after GET-1) is derived from today and never stored: Pending before the
// first start, Expired after the last end, otherwise Active. Cancelled is not reachable until F3. A
// one-term policy sold to start 2026-11-01 (TestClock.Today + 15) is covered from 2026-11-01 to
// 2027-10-31 inclusive. The two-term rows (finding A1/I5) restore that term followed by 2027-11-01 to
// 2028-10-31, so Pending is shown to measure from the first term's start and Expired from the latest term's
// end. Traited GET-1 in F2.
public sealed class PolicyStatusTests
{
    private static readonly DateOnly Start = TestClock.Today.AddDays(15);

    private static Policy TwoTermPolicy() => new PolicyBuilder().RestoreWithTerms(
        new Term(new DateOnly(2026, 11, 1), 527.18m, new Payment("PAY-DOM-0042", PaymentMethod.DirectDebit, 527.18m)),
        new Term(new DateOnly(2027, 11, 1), 561.09m, new Payment("PAY-DOM-0042-R1", PaymentMethod.DirectDebit, 561.09m)));

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_the_sale_day_before_the_start_is_Pending()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        Assert.Equal(PolicyStatus.Pending, policy.StatusOn(TestClock.Today));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_the_day_before_the_start_is_Pending()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        Assert.Equal(PolicyStatus.Pending, policy.StatusOn(new DateOnly(2026, 10, 31)));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_the_start_day_is_Active()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        Assert.Equal(PolicyStatus.Active, policy.StatusOn(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_the_end_day_is_Active()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        Assert.Equal(PolicyStatus.Active, policy.StatusOn(new DateOnly(2027, 10, 31)));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_the_day_after_the_end_is_Expired()
    {
        var policy = new PolicyBuilder().WithStartDate(Start).Build();

        Assert.Equal(PolicyStatus.Expired, policy.StatusOn(new DateOnly(2027, 11, 1)));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_the_day_before_the_first_start_of_two_terms_is_Pending()
    {
        var policy = TwoTermPolicy();

        Assert.Equal(PolicyStatus.Pending, policy.StatusOn(new DateOnly(2026, 10, 31)));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_a_day_inside_the_first_of_two_terms_is_Active()
    {
        var policy = TwoTermPolicy();

        Assert.Equal(PolicyStatus.Active, policy.StatusOn(new DateOnly(2027, 6, 1)));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_the_start_day_of_the_second_of_two_terms_is_Active()
    {
        var policy = TwoTermPolicy();

        Assert.Equal(PolicyStatus.Active, policy.StatusOn(new DateOnly(2027, 11, 1)));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_a_day_inside_the_second_of_two_terms_is_Active()
    {
        var policy = TwoTermPolicy();

        Assert.Equal(PolicyStatus.Active, policy.StatusOn(new DateOnly(2027, 11, 15)));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_the_end_day_of_the_second_of_two_terms_is_Active()
    {
        var policy = TwoTermPolicy();

        Assert.Equal(PolicyStatus.Active, policy.StatusOn(new DateOnly(2028, 10, 31)));
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public void Status_on_the_day_after_the_end_of_two_terms_is_Expired()
    {
        var policy = TwoTermPolicy();

        Assert.Equal(PolicyStatus.Expired, policy.StatusOn(new DateOnly(2028, 11, 1)));
    }
}
