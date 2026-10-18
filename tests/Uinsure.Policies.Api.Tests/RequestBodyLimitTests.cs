using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Uinsure.Policies.Api.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Api.Tests;

// The server-wide request body limit is 65536 bytes, set once on Kestrel. No Rule trait: transport hardening.
public sealed class RequestBodyLimitTests : IClassFixture<PoliciesApiFactory>
{
    private readonly PoliciesApiFactory _factory;

    public RequestBodyLimitTests(PoliciesApiFactory factory)
    {
        _factory = factory;
        _factory.ResetClock();
    }

    [Fact]
    public void Kestrel_max_request_body_size_is_65536_bytes()
    {
        var kestrel = _factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.Equal(65_536, kestrel.Limits.MaxRequestBodySize);
    }
}
