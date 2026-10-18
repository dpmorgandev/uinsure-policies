using System.Globalization;
using Uinsure.Policies.Domain.Policies;
using Xunit;

namespace Uinsure.Policies.Domain.Tests;

// SELL-1: the client's reference is trimmed and upper-cased before anything else sees it, so `abc-1`
// and `ABC-1` are the same policy. The pattern itself is checked at the 400 level, not here.
public sealed class PolicyReferenceTests
{
    [Fact]
    [Trait("Rule", "SELL-1")]
    public void Normalise_upper_cases_a_lower_case_reference()
    {
        var reference = PolicyReference.Normalise("abc-1");

        Assert.Equal("ABC-1", reference.Value);
    }

    [Theory]
    [Trait("Rule", "SELL-1")]
    [InlineData("  ABC-1")]
    [InlineData("ABC-1  ")]
    [InlineData("\tABC-1\r\n")]
    public void Normalise_trims_surrounding_whitespace(string raw)
    {
        var reference = PolicyReference.Normalise(raw);

        Assert.Equal("ABC-1", reference.Value);
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public void Normalise_leaves_an_already_normal_reference_unchanged()
    {
        var reference = PolicyReference.Normalise("HH-0042");

        Assert.Equal("HH-0042", reference.Value);
    }

    [Fact]
    [Trait("Rule", "SELL-1")]
    public void Lower_case_and_padded_upper_case_spellings_are_the_same_reference()
    {
        var lower = PolicyReference.Normalise("abc-1");
        var paddedUpper = PolicyReference.Normalise(" ABC-1 ");

        Assert.Equal(lower, paddedUpper);
    }

    // Under tr-TR, culture-sensitive upper-casing turns "i" into the dotted capital "İ" (U+0130).
    [Fact]
    [Trait("Rule", "SELL-1")]
    public void Normalise_upper_cases_with_the_invariant_culture_not_the_current_one()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var reference = PolicyReference.Normalise("pol-i");

            Assert.Equal("POL-I", reference.Value);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
