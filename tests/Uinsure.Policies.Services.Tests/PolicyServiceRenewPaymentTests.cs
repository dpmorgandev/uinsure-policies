using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Common;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Commands;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// RENEW-3a at the service: PolicyService.Renew passes the request's payment method, or none, to policy.Renew,
// which defaults it to the current term's, and the new payment's reference comes from IReferenceGenerator.Payment().
// The policy is saved only when the renewal succeeds.
//   The default stored term runs 2026-11-08 to 2027-11-07, so its window is 2027-10-08 to 2027-11-07. Every
//   renewal here is at 12:00 UTC on 2027-10-20 (BST, UK date 2027-10-20), inside it; term 2 runs 2027-11-08 to
//   2028-11-07.
public sealed class PolicyServiceRenewPaymentTests
{
    private static readonly DateTimeOffset InTheWindow = TestClock.Utc(2027, 10, 20, 12, 0);

    private readonly SequentialReferenceGenerator _references = new();

    // Card is not the stored builder's default (DirectDebit), so only the current term's method can give it.
    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public async Task Renewal_with_no_payment_method_is_paid_by_the_current_term_method()
    {
        var policy = new StoredPolicyBuilder("R3A-SVC-DEFAULT").WithPaymentMethod(PaymentMethod.Card).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RenewAt(repository, policy.Reference, 574.83m, null);

        var term = AssertRenewedAndSaved(result, policy, repository);
        Assert.Equal(new DateOnly(2027, 11, 8), term.Start);
        Assert.Equal(new Payment("PAY-SVC-1", PaymentMethod.Card, 574.83m), term.Payment);
    }

    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public async Task Renewal_with_a_payment_method_is_paid_by_that_method()
    {
        var policy = new StoredPolicyBuilder("R3A-SVC-SENT").WithPaymentMethod(PaymentMethod.Card).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RenewAt(repository, policy.Reference, 581.06m, PaymentMethod.DirectDebit);

        var term = AssertRenewedAndSaved(result, policy, repository);
        Assert.Equal(new Payment("PAY-SVC-1", PaymentMethod.DirectDebit, 581.06m), term.Payment);
    }

    // The payment reference is asked of the seam exactly once, for the new term's payment.
    [Fact]
    [Trait("Rule", "RENEW-3a")]
    public async Task Renewal_payment_reference_comes_from_the_reference_generator()
    {
        var policy = new StoredPolicyBuilder("R3A-SVC-PAYREF").Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RenewAt(repository, policy.Reference, 563.19m, null);

        var term = AssertRenewedAndSaved(result, policy, repository);
        Assert.NotNull(term.Payment);
        Assert.Equal("PAY-SVC-1", term.Payment.Reference);
        Assert.Equal(1, _references.PaymentCalls);
        Assert.Equal(0, _references.RefundCalls);
    }

    // A Cheque defaulted from the current term cannot pay an auto-renewal, so nothing is appended, saved or paid.
    [Fact]
    [Trait("Rule", "RENEW-3a")]
    [Trait("Rule", "COULD-3")]
    public async Task Renewal_defaulting_to_a_Cheque_is_rejected_and_saves_nothing()
    {
        var policy = new StoredPolicyBuilder("R3A-SVC-CHEQUE").WithPaymentMethod(PaymentMethod.Cheque).Build();
        var repository = new StoredPolicyRepository(policy);

        var result = await RenewAt(repository, policy.Reference, 563.19m, null);

        Assert.False(result.IsSuccess);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("COULD-3", violation.RuleId);
        Assert.Equal(ViolationKind.BusinessRule, violation.Kind);
        Assert.Single(policy.Terms);
        Assert.Equal(0, repository.SaveChangesCalls);
        Assert.Equal(0, repository.AddCalls);
        Assert.Equal(0, _references.PaymentCalls);
    }

    private Task<Result<PolicyDetails>> RenewAt(StoredPolicyRepository repository, PolicyReference reference, decimal amount, PaymentMethod? method) =>
        new PolicyService(repository, _references, new FakeTimeProvider(InTheWindow))
            .Renew(reference, amount, method, TestContext.Current.CancellationToken);

    // The renewal appends term 2, the policy's latest term, and returns the renewed policy as of UK today,
    // 2027-10-20: still inside term 1 (to 2027-11-07), so term 1 is current and the policy is Active.
    private static Term AssertRenewedAndSaved(Result<PolicyDetails> result, Policy policy, StoredPolicyRepository repository)
    {
        Assert.True(result.IsSuccess, "The renewal was expected to succeed.");
        Assert.Equal(2, policy.Terms.Count);
        Assert.Same(policy, result.Value.Policy);
        Assert.Same(policy.Terms[0], result.Value.CurrentTerm);
        Assert.Equal(PolicyStatus.Active, result.Value.Status);
        Assert.Equal(1, repository.SaveChangesCalls);
        Assert.Equal(0, repository.AddCalls);
        return policy.LatestTerm;
    }
}
