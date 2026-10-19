using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Support;

namespace Uinsure.Policies.Api.Tests.Builders;

// The E5 request body (RecordClaimRequest) as JSON. Build() with no overrides is a valid claim on any policy
// in force on TestClock.Today: dated today (2026-10-17) with a description. Neither is the api.md example
// (2026-12-03, "Burst pipe"). The description is optional, so a test can leave it out (WithoutDescription).
public sealed class ClaimRequestBuilder
{
    private DateOnly _claimDate = TestClock.Today;
    private string? _description = "Water damage under the kitchen sink";
    private bool _includeDescription = true;

    public ClaimRequestBuilder WithClaimDate(DateOnly value)
    {
        _claimDate = value;
        return this;
    }

    public ClaimRequestBuilder WithDescription(string? value)
    {
        _description = value;
        _includeDescription = true;
        return this;
    }

    public ClaimRequestBuilder WithoutDescription()
    {
        _includeDescription = false;
        return this;
    }

    public JsonObject Build()
    {
        var body = new JsonObject { ["claimDate"] = _claimDate.Iso() };
        if (_includeDescription)
        {
            body["description"] = _description;
        }

        return body;
    }
}
