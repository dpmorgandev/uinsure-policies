using System.Text.Json;
using Uinsure.Policies.Architecture.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// Launch profiles, read from the source file, because WebApplicationFactory never reads launchSettings.json:
// 'https' first (dotnet run's default) on 7080 and 5080, 'http' on 5080 for the loop and Postman, both
// Development, and neither trusting forwarded headers from any proxy. The behaviour proof is the HITL run.
public sealed class LaunchProfileTests
{
    private static readonly string LaunchSettings =
        RepoPaths.Combine("src", "Uinsure.Policies.Api", "Properties", "launchSettings.json");

    [Fact]
    public void Https_is_the_first_launch_profile_on_7080_and_5080()
    {
        var (name, profile) = Profiles().First();

        Assert.Equal("https", name);
        Assert.Equal("Project", profile.GetProperty("commandName").GetString());
        Assert.Equal("https://localhost:7080;http://localhost:5080", profile.GetProperty("applicationUrl").GetString());
    }

    [Fact]
    public void Http_launch_profile_serves_5080_only()
    {
        var profile = Assert.Single(Profiles(), entry => entry.Name == "http").Profile;

        Assert.Equal("Project", profile.GetProperty("commandName").GetString());
        Assert.Equal("http://localhost:5080", profile.GetProperty("applicationUrl").GetString());
    }

    [Fact]
    public void Https_and_http_profiles_run_in_Development_and_never_enable_forwarded_headers_for_any_proxy()
    {
        var profiles = Profiles();

        Assert.Equal(["https", "http"], profiles.Select(entry => entry.Name));
        Assert.All(profiles, entry =>
        {
            var environment = entry.Profile.GetProperty("environmentVariables");
            Assert.Equal("Development", environment.GetProperty("ASPNETCORE_ENVIRONMENT").GetString());
            Assert.False(environment.TryGetProperty("ASPNETCORE_FORWARDEDHEADERS_ENABLED", out _), $"Profile '{entry.Name}' trusts forwarded headers from any proxy.");
        });
    }

    private static (string Name, JsonElement Profile)[] Profiles()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(LaunchSettings));
        return [.. json.RootElement.GetProperty("profiles").EnumerateObject()
            .Select(profile => (profile.Name, profile.Value.Clone()))];
    }
}
