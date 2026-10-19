using System.Text.Json.Nodes;
using Uinsure.Policies.Api.Tests.Support;

namespace Uinsure.Policies.Api.Tests.Builders;

// The E3 request body (CancelPolicyRequest) as JSON. Build() with no overrides is a valid cancellation of any
// policy sold on TestClock.Today: 2026-11-26 (today + 40), inside cover and not in the past. It is not the
// api.md example date (2027-03-01). The body is optional (CANCEL-3), so a test can also send {} (WithoutDate),
// an explicit null (WithDate(null)) or no body at all (PolicyActions.PostCancellationWithNoBody).
public sealed class CancellationRequestBuilder
{
    private DateOnly? _cancellationDate = TestClock.Today.AddDays(40);
    private bool _includeDate = true;

    public CancellationRequestBuilder WithDate(DateOnly? value)
    {
        _cancellationDate = value;
        _includeDate = true;
        return this;
    }

    // {}: the member left out altogether, which means today.
    public CancellationRequestBuilder WithoutDate()
    {
        _includeDate = false;
        return this;
    }

    public JsonObject Build()
    {
        var body = new JsonObject();
        if (_includeDate)
        {
            body["cancellationDate"] = _cancellationDate?.Iso();
        }

        return body;
    }
}
