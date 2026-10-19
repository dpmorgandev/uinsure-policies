using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Uinsure.Policies.Api.Contracts;
using Uinsure.Policies.Api.Errors;
using Uinsure.Policies.Domain.Policies;
using Uinsure.Policies.Services;

namespace Uinsure.Policies.Api.Controllers;

[ApiController]
[Route("policies")]
public sealed class PoliciesController(IPolicyService service) : ControllerBase
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

    // Stub (F3 t2): canned bodies until the real service call replaces them.
    [HttpPost("{reference}/cancellation")]
    [EnableRateLimiting("writes")]
    public ActionResult<CancellationResponse> Cancel(
        string reference,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelPolicyRequest? request)
    {
        return StatusCode(201, new CancellationResponse(
            reference, new DateOnly(2000, 1, 1), "STUB", 7.77m,
            [new CancellationTermResponse(1, "STUB", 7, 3, 4, 7.77m)],
            [new CancellationRefundResponse("STUB-REF", Contracts.PaymentMethod.Cheque, 7.77m, "STUB-PAY")]));
    }

    [HttpGet("{reference}/cancellation-quote")]
    public ActionResult<CancellationQuoteResponse> Quote(string reference, [FromQuery, Required] DateOnly? cancellationDate)
    {
        var date = new DateOnly(2000, 1, 1);
        return new CancellationQuoteResponse(
            reference, date, "STUB", 7.77m,
            [new CancellationTermResponse(1, "STUB", 7, 3, 4, 7.77m)],
            [new QuoteRefundResponse(Contracts.PaymentMethod.Cheque, 7.77m, "STUB-PAY")],
            date);
    }

    [HttpPost("{reference}/claims")]
    [EnableRateLimiting("writes")]
    public ActionResult<RecordClaimResponse> RecordClaim(string reference, RecordClaimRequest request)
    {
        var response = new RecordClaimResponse("STUB-CLM", reference, new DateOnly(2000, 1, 1), "stub", true);
        return Created($"/policies/{reference}", response);
    }
}
