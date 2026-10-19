using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// HTTP helpers for E6 POST /policies/{reference}/renewals. RenewAndReadBack asserts the success status (201),
// Location /policies/{reference} and the PolicyResponse body in full, then reads the policy back over GET and
// asserts it in full again, with the payment references the body carried.
internal static class RenewalActions
{
    public static string RenewalsPath(string reference) => $"/policies/{reference}/renewals";

    // A renewal happens late in a term, so most tests move the clock first. Noon UTC is the same UK date in
    // BST and GMT; the BST-boundary tests set their own instant.
    public static void MoveClockTo(this PoliciesApiFactory factory, DateOnly ukDate) =>
        factory.Clock.SetUtcNow(new DateTimeOffset(ukDate.Year, ukDate.Month, ukDate.Day, 12, 0, 0, TimeSpan.Zero));

    public static Task<HttpResponseMessage> PostRenewal(this HttpClient client, string reference, JsonObject body) =>
        client.PostRenewal(reference, body.ToJsonString());

    // Raw text, so a test can send JSON that no JsonObject can represent (malformed).
    public static async Task<HttpResponseMessage> PostRenewal(this HttpClient client, string reference, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(RenewalsPath(reference), content, TestContext.Current.CancellationToken);
    }

    // No body and no Content-Type at all.
    public static Task<HttpResponseMessage> PostRenewalWithNoBody(this HttpClient client, string reference) =>
        client.PostAsync(RenewalsPath(reference), content: null, TestContext.Current.CancellationToken);

    // E6 → 201 with Location /policies/{reference} (normalised) and the PolicyResponse body. Returns the body.
    public static async Task<JsonElement> RenewedBody(HttpResponseMessage response, string normalisedReference)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/policies/{normalisedReference}", response.LocationPath());
        return await response.ReadJson();
    }

    // E6 → E2: the renewal's body and then the GET read-back, each asserted whole against the same expectation.
    // knownPaymentReferences are the references of the terms already paid before this renewal, in term order;
    // they must not change. Returns every term's payment reference (null for an unpaid term) as read back.
    public static async Task<IReadOnlyList<string?>> RenewAndReadBack(
        this HttpClient client,
        string reference,
        JsonObject body,
        JsonObject sold,
        ExpectedRenewedPolicy expected,
        IReadOnlyList<string?> knownPaymentReferences)
    {
        using var response = await client.PostRenewal(reference, body);
        var renewed = await RenewedBody(response, expected.Reference);
        var paymentReferences = RenewedPolicyAssert.ReadsBack(renewed, sold, expected, knownPaymentReferences);

        using var read = await client.GetPolicy(expected.Reference);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        _ = RenewedPolicyAssert.ReadsBack(await read.ReadJson(), sold, expected, paymentReferences);
        return paymentReferences;
    }
}
