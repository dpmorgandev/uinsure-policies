using System.Text.Json.Nodes;

namespace Uinsure.Policies.Api.Tests.Builders;

// The E6 request body (RenewPolicyRequest) as JSON. Build() with no overrides is a valid renewal of any policy
// inside its renewal window, auto-renewing or not: an amount of 603.27 and no paymentMethod, which RENEW-3a
// defaults to the current term's and RENEW-3b requires to be absent. It is not the api.md example (389.50 by
// Card). A shape test can leave the amount out (WithoutAmount) or send a member the contract does not have.
public sealed class RenewalRequestBuilder
{
    private decimal _amount = 603.27m;
    private bool _includeAmount = true;
    private string? _paymentMethod;

    public RenewalRequestBuilder WithAmount(decimal value)
    {
        _amount = value;
        _includeAmount = true;
        return this;
    }

    public RenewalRequestBuilder WithoutAmount()
    {
        _includeAmount = false;
        return this;
    }

    public RenewalRequestBuilder WithPaymentMethod(string value)
    {
        _paymentMethod = value;
        return this;
    }

    public JsonObject Build()
    {
        var body = new JsonObject();
        if (_includeAmount)
        {
            body["amount"] = _amount;
        }

        if (_paymentMethod is not null)
        {
            body["paymentMethod"] = _paymentMethod;
        }

        return body;
    }
}
