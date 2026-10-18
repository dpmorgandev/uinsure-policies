using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Uinsure.Policies.Api.Errors;

// Any exception no rule maps becomes a generic 500 problem. The exception goes to the logs only.
internal sealed class UnexpectedExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<UnexpectedExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An error occurred while processing your request.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                Detail = "An unexpected error occurred. Quote the traceId when reporting it.",
            },
        });
    }
}
