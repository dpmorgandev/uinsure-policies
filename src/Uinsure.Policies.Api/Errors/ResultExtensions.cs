using Microsoft.AspNetCore.Mvc;
using Uinsure.Policies.Domain.Common;

namespace Uinsure.Policies.Api.Errors;

internal static class ResultExtensions
{
    // A 404 or 409 arrives alone. A 422 lists every violation, in rule order, and the first names the problem.
    public static ObjectResult ToProblem(this ControllerBase controller, IReadOnlyList<RuleViolation> violations)
    {
        var first = violations[0];
        var status = ProblemCatalog.StatusFor(first.Kind);
        var single = violations.Count == 1;

        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            status,
            ProblemCatalog.TitleFor(first.RuleId),
            ProblemCatalog.TypeFor(first.RuleId),
            single ? first.Message : $"{violations.Count} rules failed.",
            controller.Request.Path);
        problem.Extensions["ruleId"] = first.RuleId;

        if (single && first.Extensions is not null)
        {
            foreach (var (key, value) in first.Extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        if (status == StatusCodes.Status422UnprocessableEntity)
        {
            problem.Extensions["violations"] = violations.Select(v => new { v.RuleId, Detail = v.Message }).ToList();
        }

        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
