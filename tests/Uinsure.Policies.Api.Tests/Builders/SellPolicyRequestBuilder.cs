using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Support;

namespace Uinsure.Policies.Api.Tests.Builders;

// The E1 request body (SellPolicyRequest) as JSON, so a shape test can also remove a member, add an
// unknown one or change a type. Build() with no overrides is a valid sale relative to TestClock.Today
// (start 2026-11-01, today + 15). Each test passes its own literal reference.
public sealed class SellPolicyRequestBuilder(string reference)
{
    private string _reference = reference;
    private string _type = "Household";
    private DateOnly _startDate = TestClock.Today.AddDays(15);
    private decimal _amount = 365.00m;
    private bool _autoRenew = true;
    private string _paymentMethod = "DirectDebit";
    private PolicyholderBuilder[] _policyholders = [new PolicyholderBuilder()];
    private string _addressLine1 = "1 High St";
    private string? _addressLine2;
    private string? _addressLine3;
    private string _postcode = "M1 1AA";

    // A valid sale whose every field differs from Build()'s defaults and from the api.md example (the
    // two match), so a read-back of it can only come from a stored sale. It starts on 2026-11-16
    // (today + 30), so it ends on 2027-11-15; each test states that end date as its own literal.
    public static SellPolicyRequestBuilder Distinctive(string reference) => new SellPolicyRequestBuilder(reference)
        .WithType("BuyToLet")
        .WithStartDate(TestClock.Today.AddDays(30))
        .WithAmount(412.34m)
        .WithAutoRenew(false)
        .WithPaymentMethod("Card")
        .WithPolicyholders(
            new PolicyholderBuilder().WithName("Grace", "Hopper").WithDateOfBirth(new DateOnly(1990, 4, 12)),
            new PolicyholderBuilder().WithName("Alan", "Turing").WithDateOfBirth(new DateOnly(1992, 6, 23)))
        .WithAddress("7 Canal St", "Flat 3", "Ancoats")
        .WithPostcode("M4 6BX");

    public SellPolicyRequestBuilder WithReference(string value)
    {
        _reference = value;
        return this;
    }

    public SellPolicyRequestBuilder WithType(string value)
    {
        _type = value;
        return this;
    }

    public SellPolicyRequestBuilder WithStartDate(DateOnly value)
    {
        _startDate = value;
        return this;
    }

    public SellPolicyRequestBuilder WithAmount(decimal value)
    {
        _amount = value;
        return this;
    }

    public SellPolicyRequestBuilder WithAutoRenew(bool value)
    {
        _autoRenew = value;
        return this;
    }

    public SellPolicyRequestBuilder WithPaymentMethod(string value)
    {
        _paymentMethod = value;
        return this;
    }

    public SellPolicyRequestBuilder WithPolicyholders(params PolicyholderBuilder[] value)
    {
        _policyholders = value;
        return this;
    }

    public SellPolicyRequestBuilder WithAddress(string addressLine1, string? addressLine2, string? addressLine3)
    {
        _addressLine1 = addressLine1;
        _addressLine2 = addressLine2;
        _addressLine3 = addressLine3;
        return this;
    }

    public SellPolicyRequestBuilder WithPostcode(string value)
    {
        _postcode = value;
        return this;
    }

    public JsonObject Build() => new()
    {
        ["reference"] = _reference,
        ["type"] = _type,
        ["startDate"] = _startDate.Iso(),
        ["amount"] = _amount,
        ["autoRenew"] = _autoRenew,
        ["paymentMethod"] = _paymentMethod,
        ["policyholders"] = new JsonArray([.. _policyholders.Select(holder => (JsonNode)holder.Build())]),
        ["property"] = new JsonObject
        {
            ["addressLine1"] = _addressLine1,
            ["addressLine2"] = _addressLine2,
            ["addressLine3"] = _addressLine3,
            ["postcode"] = _postcode,
        },
    };
}
