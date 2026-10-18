using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Uinsure.Policies.Api.Errors;

// A BadHttpRequestException keeps its own status (413, 408, 400) with a fixed title and detail: its message
// names the byte limit, so it never reaches the body. Any other exception becomes a generic 500.
// The exception goes to the logs only.
internal sealed class UnexpectedExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<UnexpectedExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException badRequest)
        {
            return await WriteBadRequest(httpContext, badRequest);
        }

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

    private async ValueTask<bool> WriteBadRequest(HttpContext httpContext, BadHttpRequestException exception)
    {
        logger.LogWarning(exception, "Bad request on {Method} {Path}: {Status}", httpContext.Request.Method, httpContext.Request.Path, exception.StatusCode);

        var (title, detail) = exception.StatusCode switch
        {
            StatusCodes.Status413PayloadTooLarge => ("Request body too large.", "The request body is larger than this API accepts."),
            StatusCodes.Status408RequestTimeout => ("Request timed out.", "The request was not received in time."),
            _ => ("Bad request.", "The request could not be read."),
        };

        httpContext.Response.StatusCode = exception.StatusCode;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = exception.StatusCode, Title = title, Detail = detail },
        });
    }
}
