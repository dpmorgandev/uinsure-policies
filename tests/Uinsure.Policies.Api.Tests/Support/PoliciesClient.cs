using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// HTTP helpers for the /policies routes. Every Sell test is a POST → GET round trip through these.
internal static class PoliciesClient
{
    public static Task<HttpResponseMessage> PostPolicy(this HttpClient client, JsonObject body) =>
        client.PostPolicy(body.ToJsonString());

    // Raw text, so a test can send JSON that no JsonObject can represent (malformed, truncated).
    public static async Task<HttpResponseMessage> PostPolicy(this HttpClient client, string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.PostAsync("/policies", content, TestContext.Current.CancellationToken);
    }

    public static Task<HttpResponseMessage> GetPolicy(this HttpClient client, string reference) =>
        client.GetAsync("/policies/" + Uri.EscapeDataString(reference), TestContext.Current.CancellationToken);

    public static async Task<JsonElement> ReadJson(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    // The Location header's path, whether the app writes it absolute or relative.
    public static string LocationPath(this HttpResponseMessage response)
    {
        var location = response.Headers.Location;
        Assert.NotNull(location);
        return location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
    }

    // POST → 201 with Location /policies/{expectedReference} → GET that Location → 200.
    // Returns the policy as read back, so the test asserts what was stored, not what was echoed.
    public static async Task<JsonElement> SellAndReadBack(this HttpClient client, JsonObject body, string expectedReference)
    {
        using var sold = await client.PostPolicy(body);
        Assert.Equal(HttpStatusCode.Created, sold.StatusCode);
        var location = sold.LocationPath();
        Assert.Equal($"/policies/{expectedReference}", location);

        using var read = await client.GetAsync(location, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var policy = await read.ReadJson();
        Assert.Equal(expectedReference, policy.GetProperty("reference").GetString());
        return policy;
    }

    // The round trip of a rejected sale: nothing was stored under the reference.
    public static async Task AssertNotStored(this HttpClient client, string reference)
    {
        using var read = await client.GetPolicy(reference);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    // A DateOnly member, which the contract writes as yyyy-MM-dd.
    public static DateOnly GetDate(this JsonElement element, string property) =>
        DateOnly.ParseExact(element.GetProperty(property).GetString() ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Iso(this DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
