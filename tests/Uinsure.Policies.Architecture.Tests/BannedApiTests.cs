using System.Text.Json;
using System.Text.RegularExpressions;
using Uinsure.Policies.Architecture.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

// Banned APIs: BannedSymbols.txt lists the clock and rounding calls, every src project restores
// BannedApiAnalyzers, and A11: the RS0030 suppression appears only in Money.cs.
public sealed partial class BannedApiTests
{
    [Theory]
    [InlineData("P:System.DateTime.Now")]
    [InlineData("P:System.DateTime.UtcNow")]
    [InlineData("P:System.DateTime.Today")]
    [InlineData("P:System.DateTimeOffset.Now")]
    [InlineData("P:System.DateTimeOffset.UtcNow")]
    [InlineData("M:System.TimeProvider.GetLocalNow")]
    [InlineData("P:System.TimeZoneInfo.Local")]
    [InlineData("M:System.Math.Round(System.Decimal)")]
    [InlineData("M:System.Math.Round(System.Decimal,System.Int32)")]
    [InlineData("M:System.Math.Round(System.Decimal,System.MidpointRounding)")]
    [InlineData("M:System.Math.Round(System.Decimal,System.Int32,System.MidpointRounding)")]
    [InlineData("M:System.Decimal.Round(System.Decimal)")]
    [InlineData("M:System.Decimal.Round(System.Decimal,System.Int32)")]
    [InlineData("M:System.Decimal.Round(System.Decimal,System.MidpointRounding)")]
    [InlineData("M:System.Decimal.Round(System.Decimal,System.Int32,System.MidpointRounding)")]
    public void BannedSymbols_file_bans_the_symbol(string documentationId)
    {
        var path = RepoPaths.Combine("BannedSymbols.txt");
        Assert.True(File.Exists(path), "BannedSymbols.txt is missing from the repository root.");

        var banned = File.ReadAllLines(path)
            .Select(line => line.Split(';')[0].Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal) && !line.StartsWith('#'));

        Assert.Contains(documentationId, banned);
    }

    [Theory]
    [InlineData("Uinsure.Policies.Domain")]
    [InlineData("Uinsure.Policies.Services")]
    [InlineData("Uinsure.Policies.Persistence")]
    [InlineData("Uinsure.Policies.Api")]
    public void Banned_API_analyzer_is_restored_for_the_src_project(string project)
    {
        var assets = new[]
        {
            RepoPaths.Combine("src", project, "obj", "project.assets.json"),
            RepoPaths.Combine("artifacts", "obj", project, "project.assets.json"),
        }.FirstOrDefault(File.Exists);
        Assert.NotNull(assets);

        using var json = JsonDocument.Parse(File.ReadAllText(assets));
        var libraries = json.RootElement.GetProperty("libraries").EnumerateObject().Select(library => library.Name);

        Assert.Contains(libraries, name => name.StartsWith("Microsoft.CodeAnalysis.BannedApiAnalyzers/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RS0030_is_suppressed_only_in_Money_cs()
    {
        var offending = SuppressionCandidateFiles()
            .Where(file => Path.GetFileName(file) != "Money.cs")
            .Where(file => SuppressesRs0030(File.ReadAllText(file)))
            .Select(RepoPaths.Relative);

        Assert.Empty(offending);
    }

    private static IEnumerable<string> SuppressionCandidateFiles()
    {
        string[] extensions = [".cs", ".csproj", ".props", ".targets", ".editorconfig", ".globalconfig"];
        var src = RepoPaths.Combine("src");
        var underSrc = Directory.Exists(src)
            ? Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
                .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    || Path.GetFileName(file) == ".editorconfig")
                .Where(file => !RepoPaths.IsBuildOutput(RepoPaths.Relative(file)))
            : [];
        var atRoot = new[] { "Directory.Build.props", "Directory.Build.targets", ".editorconfig", ".globalconfig" }
            .Select(name => RepoPaths.Combine(name))
            .Where(File.Exists);

        return underSrc.Concat(atRoot);
    }

    private static bool SuppressesRs0030(string text) =>
        PragmaDisable().IsMatch(text)
        || SuppressMessage().IsMatch(text)
        || NoWarn().IsMatch(text)
        || SeverityLowered().IsMatch(text);

    [GeneratedRegex(@"#pragma\s+warning\s+disable\b[^\r\n]*\bRS0030\b")]
    private static partial Regex PragmaDisable();

    [GeneratedRegex(@"SuppressMessage\s*\([^)]*\bRS0030\b")]
    private static partial Regex SuppressMessage();

    [GeneratedRegex(@"<NoWarn>[^<]*\bRS0030\b")]
    private static partial Regex NoWarn();

    [GeneratedRegex(@"dotnet_diagnostic\.RS0030\.severity\s*=\s*(none|silent|suggestion)")]
    private static partial Regex SeverityLowered();
}
