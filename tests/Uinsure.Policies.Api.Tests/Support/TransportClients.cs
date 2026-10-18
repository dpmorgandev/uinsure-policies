using Microsoft.AspNetCore.Mvc.Testing;

namespace Uinsure.Policies.Api.Tests.Support;

// Clients for the transport tests. The in-process TestServer takes the request's scheme and host from the
// client's base address, so these choose plain HTTP or HTTPS. The host is not localhost, 127.0.0.1 or [::1],
// which HSTS skips. Redirects are never followed: the API must reject plain HTTP, not redirect it.
internal static class TransportClients
{
    public static Uri HttpsBaseAddress { get; } = new("https://policies.example.test");

    public static Uri HttpBaseAddress { get; } = new("http://policies.example.test");

    public static HttpClient CreateHttpsClient(this WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = HttpsBaseAddress, AllowAutoRedirect = false });

    public static HttpClient CreateHttpClient(this WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = HttpBaseAddress, AllowAutoRedirect = false });
}
