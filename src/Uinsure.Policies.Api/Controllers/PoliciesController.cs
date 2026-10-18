using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Uinsure.Policies.Api.Contracts;
using Uinsure.Policies.Api.Errors;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services;

namespace Uinsure.Policies.Api.Controllers;

[ApiController]
[Route("policies")]
public sealed class PoliciesController(PolicyService service) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("writes")]
    public async Task<ActionResult<PolicyResponse>> Sell(SellPolicyRequest request, CancellationToken cancellationToken)
    {
        var result = await service.Sell(request.ToCommand(), cancellationToken);
        if (!result.IsSuccess)
        {
            return this.ToProblem(result.Violations);
        }

        var response = result.Value.ToResponse();
        return Created($"/policies/{response.Reference}", response);
    }

    [HttpGet("{reference}")]
    public async Task<ActionResult<PolicyResponse>> Get(string reference, CancellationToken cancellationToken)
    {
        var result = await service.Get(PolicyReference.Normalise(reference), cancellationToken);
        return result.IsSuccess ? result.Value.ToResponse() : this.ToProblem(result.Violations);
    }
}
