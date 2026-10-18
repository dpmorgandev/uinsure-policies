using Xunit;

namespace Uinsure.Policies.Services.Tests;

// SELL-6: the generated payment reference has the contract's "PAY-…" form and is unique per call.
public sealed class ReferenceGeneratorTests
{
    [Fact]
    [Trait("Rule", "SELL-6")]
    public void Payment_reference_starts_with_PAY_and_has_a_generated_part()
    {
        var reference = ReferenceGenerator.Payment();

        Assert.StartsWith("PAY-", reference, StringComparison.Ordinal);
        Assert.True(reference.Length > "PAY-".Length, "Expected a generated part after 'PAY-'.");
    }

    [Fact]
    [Trait("Rule", "SELL-6")]
    public void Two_payment_references_differ()
    {
        Assert.NotEqual(ReferenceGenerator.Payment(), ReferenceGenerator.Payment());
    }
}
