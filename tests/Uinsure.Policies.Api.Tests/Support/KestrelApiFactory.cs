namespace Uinsure.Policies.Api.Tests.Support;

// The same API in Development, but on real Kestrel on a dynamic local port instead of the in-process
// TestServer, which doesn't enforce Kestrel's limits. Development, so plain HTTP passes the HTTPS check.
public sealed class KestrelApiFactory : PoliciesApiFactory
{
    public KestrelApiFactory() => UseKestrel(0);
}
