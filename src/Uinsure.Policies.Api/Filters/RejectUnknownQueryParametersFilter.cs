using Microsoft.AspNetCore.Mvc.Filters;
using Uinsure.Policies.Api.Errors;

namespace Uinsure.Policies.Api.Filters;

// Strict input: a query key the action does not bind is a 400, in the same shape as any other shape error.
public sealed class RejectUnknownQueryParametersFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var known = context.ActionDescriptor.Parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = context.HttpContext.Request.Query.Keys.Where(key => !known.Contains(key)).ToList();
        if (unknown.Count == 0)
        {
            return;
        }

        foreach (var key in unknown)
        {
            context.ModelState.AddModelError(key, "Unknown query parameter.");
        }

        context.Result = ValidationProblems.FromModelState(context);
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
