using System.ComponentModel.DataAnnotations;
using System.Globalization;
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

    [HttpPost("{reference}/cancellation")]
    [EnableRateLimiting("writes")]
    public async Task<ActionResult<CancellationResponse>> Cancel(
        string reference,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelPolicyRequest? request,
        CancellationToken cancellationToken)
    {
        var policyReference = PolicyReference.Normalise(reference);
        var result = await service.Cancel(policyReference, request?.CancellationDate, cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value.ToResponse(policyReference)) : this.ToProblem(result.Violations);
    }

    [HttpGet("{reference}/cancellation-quote")]
    public async Task<ActionResult<CancellationQuoteResponse>> Quote(
        string reference,
        [FromQuery, Required] string? cancellationDate,
        CancellationToken cancellationToken)
    {
        // Strict input: the model binder for a query DateOnly also takes "11/30/2026", so the format is checked here.
        if (!DateOnly.TryParseExact(cancellationDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            ModelState.AddModelError(nameof(cancellationDate), "The cancellation date must be a date in the form yyyy-MM-dd.");
            return ValidationProblem();
        }

        var policyReference = PolicyReference.Normalise(reference);
        var quote = await service.QuoteCancellation(policyReference, date, cancellationToken);
        return quote.IsSuccess ? quote.Value.ToResponse(policyReference) : this.ToProblem(quote.Violations);
    }

    [HttpPost("{reference}/claims")]
    [EnableRateLimiting("writes")]
    public async Task<ActionResult<RecordClaimResponse>> RecordClaim(string reference, RecordClaimRequest request, CancellationToken cancellationToken)
    {
        var policyReference = PolicyReference.Normalise(reference);
        var recorded = await service.RecordClaim(policyReference, request.ClaimDate!.Value, request.Description, cancellationToken);
        if (!recorded.IsSuccess)
        {
            return this.ToProblem(recorded.Violations);
        }

        var response = recorded.Value.ToResponse(policyReference);
        return Created($"/policies/{response.PolicyReference}", response);
    }

    [HttpPost("{reference}/renewals")]
    [EnableRateLimiting("writes")]
    public async Task<ActionResult<PolicyResponse>> Renew(
        string reference,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RenewPolicyRequest? request,
        CancellationToken cancellationToken)
    {
        // The amount is required, so a missing body is a shape error (400), not a 415.
        if (request is null)
        {
            ModelState.AddModelError(nameof(RenewPolicyRequest.Amount), "A request body with an amount is required.");
            return ValidationProblem();
        }

        var policyReference = PolicyReference.Normalise(reference);
        var renewal = await service.Renew(policyReference, request.Amount!.Value, request.PaymentMethod?.ToDomain(), cancellationToken);
        if (!renewal.IsSuccess)
        {
            return this.ToProblem(renewal.Violations);
        }

        return Created($"/policies/{policyReference.Value}", renewal.Value.ToResponse());
    }
}
