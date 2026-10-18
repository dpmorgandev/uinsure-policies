using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Uinsure.Policies.Api.Errors;
using Scalar.AspNetCore;
using Uinsure.Policies.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Strict input: unknown members, integer enums and string numbers are 400s.
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddOpenApi();

// RFC 9457 bodies: every problem response carries the request path and a traceId.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Instance = context.HttpContext.Request.Path;
    context.ProblemDetails.Extensions["traceId"] =
        System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
});

builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();

if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

app.Services.EnsureSqliteCreated();

// Logs the exception server-side and answers a generic 500 problem; no message or stack trace in the body.
app.UseExceptionHandler();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program;
