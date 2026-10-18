using Microsoft.Extensions.Hosting;

namespace Uinsure.Policies.Api.Tests.Support;

// The same throwing paths in Development, where a BadHttpRequestException's message must still stay out
// of the body, because it names the byte limit.
public sealed class DevelopmentBadRequestExceptionApiFactory : BadRequestExceptionApiFactory
{
    protected override string EnvironmentName => Environments.Development;
}
