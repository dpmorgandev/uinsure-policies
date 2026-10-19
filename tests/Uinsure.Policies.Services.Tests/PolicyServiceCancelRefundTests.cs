using Microsoft.Extensions.Time.Testing;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services.Tests.Builders;
using Uinsure.Policies.Services.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Services.Tests;

// CANCEL-2 at the seam: every refund a cancellation creates takes its reference from the injected
// IReferenceGenerator.Refund(), one call per refund, and names the payment it reverses.
//   Terms 2026-10-01 to 2027-09-30 (365 days) and 2027-10-01 to 2028-09-30 (366 days), each 527.18 by direct
//   debit, paid as PAY-C2-SVC-SEAM-1 and PAY-C2-SVC-SEAM-2. Cancelled on today, 2026-10-17: the first term is
//   pro-rata, 527.18 × 348 ÷ 365 = 502.6264… → 502.63; the second is before its start, a full 527.18.
public sealed class PolicyServiceCancelRefundTests
{
    [Fact]
    [Trait("Rule", "CANCEL-2")]
    public async Task Each_refund_takes_its_reference_from_the_reference_generator()
    {
        var policy = new StoredPolicyBuilder("C2-SVC-SEAM")
            .WithTermsStarting(new DateOnly(2026, 10, 1), new DateOnly(2027, 10, 1))
            .Build();
        var references = new SequentialReferenceGenerator();
        var service = new PolicyService(new StoredPolicyRepository(policy), references, new FakeTimeProvider(TestClock.Now));

        var result = await service.Cancel(policy.Reference, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, "The cancellation was expected to succeed.");
        Assert.Equal(
            [
                new Refund("REF-SVC-1", PaymentMethod.DirectDebit, 502.63m, "PAY-C2-SVC-SEAM-1"),
                new Refund("REF-SVC-2", PaymentMethod.DirectDebit, 527.18m, "PAY-C2-SVC-SEAM-2"),
            ],
            result.Value.Refunds);
        Assert.Equal(2, references.RefundCalls);
        Assert.Equal(0, references.PaymentCalls);
    }
}
