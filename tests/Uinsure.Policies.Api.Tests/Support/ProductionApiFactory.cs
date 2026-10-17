using Microsoft.Extensions.Hosting;

namespace Uinsure.Policies.Api.Tests.Support;

// The same API in the Production environment: no OpenAPI, no Scalar, no developer details.
public class ProductionApiFactory : PoliciesApiFactory
{
    protected override string EnvironmentName => Environments.Production;
}
