using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Uinsure.Policies.Api.Errors;

// The 400 for a shape error: ValidationProblemDetails whose errors keys use the JSON names the client sent
// ("policyholders[0].dateOfBirth"), not the C# member names model state records.
internal static class ValidationProblems
{
    public static IActionResult FromModelState(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateValidationProblemDetails(context.HttpContext, context.ModelState);

        var errors = problem.Errors.ToDictionary(error => CamelCase(error.Key), error => error.Value);
        problem.Errors.Clear();
        foreach (var (key, messages) in errors)
        {
            problem.Errors[key] = messages;
        }

        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    private static string CamelCase(string key) =>
        string.Join('.', key.Split('.').Select(segment => segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
}
