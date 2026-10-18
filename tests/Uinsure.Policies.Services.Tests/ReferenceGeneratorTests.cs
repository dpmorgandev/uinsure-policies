using Xunit;

namespace Uinsure.Policies.Services.Tests;

// SELL-6: the generated payment reference has the contract's "PAY-…" form and is unique per call.
// Called through IReferenceGenerator, the only way in (finding A1/I4).
public sealed class ReferenceGeneratorTests
{
    private readonly IReferenceGenerator _generator = new ReferenceGenerator();

    [Fact]
    [Trait("Rule", "SELL-6")]
    public void Payment_reference_starts_with_PAY_and_has_a_generated_part()
    {
        var reference = _generator.Payment();

        Assert.StartsWith("PAY-", reference, StringComparison.Ordinal);
        Assert.True(reference.Length > "PAY-".Length, "Expected a generated part after 'PAY-'.");
    }

    [Fact]
    [Trait("Rule", "SELL-6")]
    public void Two_payment_references_differ()
    {
        Assert.NotEqual(_generator.Payment(), _generator.Payment());
    }
}
