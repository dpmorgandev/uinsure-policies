namespace Uinsure.Policies.Api.Errors;

// A client that goes away is not a server fault. ASP.NET Core's exception handler already answers it with
// 499 and a Debug entry, so this notes it at Information (method and path only, never the body or client IP)
// and lets the exception carry on to that handler.
internal sealed class ClientAbortLoggingMiddleware(RequestDelegate next, ILogger<ClientAbortLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Client aborted {Method} {Path}", context.Request.Method, context.Request.Path);
            throw;
        }
    }
}
