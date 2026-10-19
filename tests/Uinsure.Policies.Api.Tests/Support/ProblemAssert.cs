using System.Net;
using System.Text.Json;
using Xunit;

namespace Uinsure.Policies.Api.Tests.Support;

// RFC 9457 bodies per the API contract. Status first, then the body.
internal static class ProblemAssert
{
    // A 422 with exactly one violation: the rule's own type, title and ruleId at the top level, and its
    // detail repeated as the single violations[] entry. Returns the body for the extension assertions.
    public static async Task<JsonElement> SingleRuleViolation(HttpResponseMessage response, string ruleId, string title, string instance = "/policies")
    {
        var problem = await RuleProblem(response, HttpStatusCode.UnprocessableEntity, ruleId, title, instance);

        var violation = Assert.Single(problem.GetProperty("violations").EnumerateArray());
        Assert.Equal(ruleId, violation.GetProperty("ruleId").GetString());
        Assert.Equal(problem.GetProperty("detail").GetString(), violation.GetProperty("detail").GetString());
        return problem;
    }

    // A 422 with several violations: the first rule (in rule-ID order) names the problem, the detail counts the
    // rules, and violations[] lists exactly the expected rules in that order, each with a detail of its own.
    public static async Task<JsonElement> MultipleRuleViolations(HttpResponseMessage response, string title, string instance, params string[] ruleIds)
    {
        var problem = await RuleProblem(response, HttpStatusCode.UnprocessableEntity, ruleIds[0], title, instance);

        Assert.Equal($"{ruleIds.Length} rules failed.", problem.GetProperty("detail").GetString());
        var violations = problem.GetProperty("violations").EnumerateArray().ToArray();
        Assert.Equal(ruleIds, violations.Select(violation => violation.GetProperty("ruleId").GetString()).ToArray());
        Assert.All(violations, violation => Assert.False(string.IsNullOrWhiteSpace(violation.GetProperty("detail").GetString())));
        return problem;
    }

    // A 409 state conflict: short-circuits before the 422 rules, so it carries no violations[].
    public static async Task<JsonElement> Conflict(HttpResponseMessage response, string ruleId, string title, string instance = "/policies")
    {
        var problem = await RuleProblem(response, HttpStatusCode.Conflict, ruleId, title, instance);

        Assert.False(problem.TryGetProperty("violations", out _));
        return problem;
    }

    // GET-1: a 404 for a reference with no policy. It short-circuits before every rule, so it carries no
    // violations[]; the detail and the reference extension name the normalised reference.
    public static async Task<JsonElement> NotFound(HttpResponseMessage response, string instance, string normalisedReference)
    {
        var problem = await RuleProblem(response, HttpStatusCode.NotFound, "GET-1", "Policy not found", instance);

        Assert.Equal($"No policy with reference '{normalisedReference}'.", problem.Member("detail").GetString());
        Assert.Equal(normalisedReference, problem.Member("reference").GetString());
        Assert.False(problem.TryGetProperty("violations", out _));
        return problem;
    }

    // The members every rule problem carries: type /problems/{rule-id lower-case}, title, status, detail,
    // instance (the request path: /policies for a sale, /policies/{reference} as sent for a GET), ruleId
    // and traceId.
    public static async Task<JsonElement> RuleProblem(HttpResponseMessage response, HttpStatusCode status, string ruleId, string title, string instance = "/policies")
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.ReadJson();
        Assert.Equal($"/problems/{ruleId.ToLowerInvariant()}", problem.GetProperty("type").GetString());
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
        Assert.Equal(instance, problem.GetProperty("instance").GetString());
        Assert.Equal(ruleId, problem.GetProperty("ruleId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        return problem;
    }

    // A transport problem (the HTTPS check, the rate limiter, a BadHttpRequestException): RFC 9457 with a
    // title, a detail, the request path as instance and a traceId, and no ruleId, because no rule is broken.
    // The type is checked where the contract fixes one. Returns the body.
    public static async Task<JsonElement> TransportProblem(HttpResponseMessage response, HttpStatusCode status, string instance, string? type = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.ReadJson();
        if (type is not null)
        {
            Assert.Equal(type, problem.GetProperty("type").GetString());
        }

        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
        Assert.Equal(instance, problem.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.False(problem.TryGetProperty("ruleId", out _));
        return problem;
    }

    // A 400 shape error: ValidationProblemDetails with a non-empty errors map and a traceId, no ruleId.
    // Returns the errors map.
    public static async Task<JsonElement> ValidationErrors(HttpResponseMessage response, string instance = "/policies")
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.ReadJson();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("One or more validation errors occurred.", problem.GetProperty("title").GetString());
        Assert.Equal(instance, problem.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.False(problem.TryGetProperty("ruleId", out _));

        var errors = problem.GetProperty("errors");
        Assert.Equal(JsonValueKind.Object, errors.ValueKind);
        Assert.NotEmpty(errors.EnumerateObject());
        return errors;
    }

    // An errors-map entry for the field, keyed as the contract writes it (camelCase JSON names,
    // "$.x" for a JSON-reader error).
    public static void HasError(JsonElement errors, string key)
    {
        Assert.True(errors.TryGetProperty(key, out var messages), $"errors has no '{key}' entry: {errors}");
        Assert.NotEmpty(messages.EnumerateArray());
    }
}
