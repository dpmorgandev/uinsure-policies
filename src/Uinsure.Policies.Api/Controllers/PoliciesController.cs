using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Uinsure.Policies.Api.Contracts;

namespace Uinsure.Policies.Api.Controllers;

[ApiController]
[Route("policies")]
public sealed class PoliciesController : ControllerBase
{
    // STUB: canned responses until the green steps wire the service.
    [HttpPost]
    [EnableRateLimiting("writes")]
    public ActionResult<PolicyResponse> Sell(SellPolicyRequest request, CancellationToken cancellationToken)
    {
        var reference = request.Reference!.Trim().ToUpperInvariant();
        return Created($"/policies/{reference}", Canned(reference));
    }

    [HttpGet("{reference}")]
    public ActionResult<PolicyResponse> Get(string reference, CancellationToken cancellationToken) =>
        Canned(reference.Trim().ToUpperInvariant());

    private static PolicyResponse Canned(string reference) => new(
        reference,
        PolicyType.Household,
        new DateOnly(2026, 11, 1),
        365.00m,
        true,
        new PropertyDto { AddressLine1 = "1 High St", Postcode = "M1 1AA" });
}
