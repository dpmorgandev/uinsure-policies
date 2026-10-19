using Uinsure.Policies.Api.Tests.Builders;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// E3, the shape of the optional cancellation body (CANCEL-3) and an unknown reference. A body that is sent must
// be valid: malformed JSON, an unknown field or a date that is not yyyy-MM-dd is a 400 ValidationProblemDetails
// with no ruleId, returned before any rule, and the policy is left as sold. An unknown reference is 404 GET-1.
//   start 2026-11-08 ends 2027-11-07 (the builder's default sale).
public sealed class CancelShapeTests : IClassFixture<PoliciesApiFactory>
{
    private static readonly DateOnly DefaultEnd = new(2027, 11, 7);

    private readonly PoliciesApiFactory _factory;

    public CancelShapeTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_with_an_unknown_field_is_rejected_with_400()
    {
        var body = new CancellationRequestBuilder().Build();
        body["reason"] = "Moving abroad";

        await AssertShape("CAN-SHAPE-UNKNOWN", body.ToJsonString(), "$.reason");
    }

    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_with_a_cancellationDate_not_in_yyyy_MM_dd_is_rejected_with_400()
    {
        await AssertShape("CAN-SHAPE-BAD-DATE", "{\"cancellationDate\": \"26/11/2026\"}", "$.cancellationDate");
    }

    [Fact]
    [Trait("Rule", "CANCEL-3")]
    public async Task Cancel_with_malformed_JSON_is_rejected_with_400()
    {
        await AssertShape("CAN-SHAPE-MALFORMED", "{\"cancellationDate\": \"2026-11-26\"", errorKey: null);
    }

    [Fact]
    [Trait("Rule", "GET-1")]
    public async Task Cancel_of_an_unknown_reference_answers_404()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostCancellation("CAN-NO-SUCH", new CancellationRequestBuilder().Build());

        await ProblemAssert.NotFound(response, PolicyActions.CancellationPath("CAN-NO-SUCH"), "CAN-NO-SUCH");
    }

    // errorKey null: malformed JSON, whose errors entry the JSON reader names.
    private async Task AssertShape(string reference, string json, string? errorKey)
    {
        var sold = new SellPolicyRequestBuilder(reference).Build();
        using var client = _factory.CreateClient();
        await client.SellAndReadBack(sold, reference);

        using var response = await client.PostCancellation(reference, json);

        var errors = await ProblemAssert.ValidationErrors(response, PolicyActions.CancellationPath(reference));
        if (errorKey is not null)
        {
            ProblemAssert.HasError(errors, errorKey);
        }

        PolicyStateAssert.ReadsBackUnchanged(await client.ReadBack(reference), sold, reference, DefaultEnd, "Pending");
    }
}
