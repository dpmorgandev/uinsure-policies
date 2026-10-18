using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// Requests for the transport tests, sent from a chosen client IP (RemoteIpStartupFilter) with optional
// forwarded headers.
internal static class TransportRequests
{
    public static HttpRequestMessage Get(string path) => new(HttpMethod.Get, path);

    public static HttpRequestMessage Sell(JsonObject body) => new(HttpMethod.Post, "/policies")
    {
        Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    public static HttpRequestMessage From(this HttpRequestMessage request, string remoteIp)
    {
        request.Headers.Add(RemoteIpStartupFilter.Header, remoteIp);
        return request;
    }

    public static HttpRequestMessage WithHeader(this HttpRequestMessage request, string name, string value)
    {
        request.Headers.TryAddWithoutValidation(name, value);
        return request;
    }

    public static async Task<HttpResponseMessage> SendOnce(this HttpClient client, HttpRequestMessage request, CancellationToken? cancellationToken = null)
    {
        using (request)
        {
            return await client.SendAsync(request, cancellationToken ?? TestContext.Current.CancellationToken);
        }
    }
}
