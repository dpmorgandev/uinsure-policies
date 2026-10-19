using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// HTTP helpers for the F3 routes: E3 POST /policies/{reference}/cancellation, E4 GET
// /policies/{reference}/cancellation-quote?cancellationDate= and E5 POST /policies/{reference}/claims.
// The Cancel and RecordClaim helpers assert the success status (201) and the Location rule of each route;
// the test then reads the policy back over GET and asserts it in full.
internal static class PolicyActions
{
    public static string CancellationPath(string reference) => $"/policies/{reference}/cancellation";

    public static string QuotePath(string reference) => $"/policies/{reference}/cancellation-quote";

    public static string ClaimsPath(string reference) => $"/policies/{reference}/claims";

    public static Task<HttpResponseMessage> PostCancellation(this HttpClient client, string reference, JsonObject body) =>
        client.PostJson(CancellationPath(reference), body.ToJsonString());

    // Raw text, so a test can send JSON that no JsonObject can represent (malformed, an empty string).
    public static Task<HttpResponseMessage> PostCancellation(this HttpClient client, string reference, string json) =>
        client.PostJson(CancellationPath(reference), json);

    // No body and no Content-Type at all: the optional body of CANCEL-3.
    public static Task<HttpResponseMessage> PostCancellationWithNoBody(this HttpClient client, string reference) =>
        client.PostAsync(CancellationPath(reference), content: null, TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> GetQuote(this HttpClient client, string reference, DateOnly cancellationDate) =>
        client.GetQuote(reference, "cancellationDate=" + cancellationDate.Iso());

    // The query string as sent (without the "?"), so a shape test can leave the date out or misspell it.
    public static Task<HttpResponseMessage> GetQuote(this HttpClient client, string reference, string query) =>
        client.GetAsync(query.Length == 0 ? QuotePath(reference) : $"{QuotePath(reference)}?{query}", TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PostClaim(this HttpClient client, string reference, JsonObject body) =>
        client.PostJson(ClaimsPath(reference), body.ToJsonString());

    public static Task<HttpResponseMessage> PostClaim(this HttpClient client, string reference, string json) =>
        client.PostJson(ClaimsPath(reference), json);

    // E3 → 201 with the CancellationResponse body and no Location (api.md E3). Returns the body.
    public static async Task<JsonElement> Cancel(this HttpClient client, string reference, JsonObject body)
    {
        using var response = await client.PostCancellation(reference, body);
        return await CancelledBody(response);
    }

    public static async Task<JsonElement> CancelledBody(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        return await response.ReadJson();
    }

    // E4 → 200 with the CancellationQuoteResponse body. Returns the body.
    public static async Task<JsonElement> Quote(this HttpClient client, string reference, DateOnly cancellationDate)
    {
        using var response = await client.GetQuote(reference, cancellationDate);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJson();
    }

    // E5 → 201 with Location /policies/{reference} and the ClaimResponse body. Returns the body.
    public static async Task<JsonElement> RecordClaim(this HttpClient client, string reference, JsonObject body)
    {
        using var response = await client.PostClaim(reference, body);
        return await ClaimedBody(response, reference);
    }

    public static async Task<JsonElement> ClaimedBody(HttpResponseMessage response, string reference)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/policies/{reference}", response.LocationPath());
        return await response.ReadJson();
    }

    // GET /policies/{reference} → 200, the policy as stored now.
    public static async Task<JsonElement> ReadBack(this HttpClient client, string reference)
    {
        using var read = await client.GetPolicy(reference);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        return await read.ReadJson();
    }

    // The reference of the one payment a sold policy has (SELL-6), which its refund must reverse (CANCEL-2).
    public static string PaymentReference(this JsonElement policy)
    {
        var payment = Assert.Single(policy.Member("payments").EnumerateArray());
        var reference = payment.Member("reference").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reference));
        return reference;
    }

    private static async Task<HttpResponseMessage> PostJson(this HttpClient client, string path, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync(path, content, TestContext.Current.CancellationToken);
    }
}
