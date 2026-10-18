using Uinsure.Policies.Architecture.Tests.Support;
using Xunit;

namespace Uinsure.Policies.Architecture.Tests;

public sealed class RuleCoverageTests
{
    private const string UnitProject = "Uinsure.Policies.Domain.Tests";
    private const string IntegrationProject = "Uinsure.Policies.Api.Tests";

    // The repository's own coverage: these run in every dotnet test.

    [Fact]
    public void Every_rule_id_that_is_not_pending_has_a_unit_and_an_integration_trait()
    {
        Assert.Empty(RuleCoverage.ForRepository(RepoPaths.Root).MissingCoverage);
    }

    [Fact]
    public void Every_rule_trait_names_a_rule_id_from_the_requirements()
    {
        Assert.Empty(RuleCoverage.ForRepository(RepoPaths.Root).UnknownTraits);
    }

    [Fact]
    public void No_pending_rule_id_already_has_a_rule_trait()
    {
        Assert.Empty(RuleCoverage.ForRepository(RepoPaths.Root).StalePending);
    }

    [Fact]
    public void Every_pending_id_is_a_rule_id_from_the_requirements()
    {
        Assert.Empty(RuleCoverage.ForRepository(RepoPaths.Root).UnknownPending);
    }

    [Fact]
    public void Requirements_rule_tables_parse_with_RENEW_1_2_expanded_into_two_ids()
    {
        var ids = RuleCoverage.ParseRuleIds(File.ReadAllText(RepoPaths.Combine("docs", "requirements.md")));

        Assert.Contains("SELL-1", ids);
        Assert.Contains("CANCEL-1a", ids);
        Assert.Contains("RENEW-1", ids);
        Assert.Contains("RENEW-2", ids);
        Assert.Contains("COULD-3", ids);
        Assert.DoesNotContain("RENEW-1/2", ids);
    }

    // The check itself: each failure mode is detected, so a green run means something.

    [Fact]
    public void A_built_rule_with_no_unit_trait_is_reported()
    {
        var report = RuleCoverage.Analyse(Ids("SELL-1"), Traits(IntegrationProject, "SELL-1"), Ids());

        Assert.Single(report.MissingCoverage, gap => gap.StartsWith("SELL-1: no unit-test trait", StringComparison.Ordinal));
    }

    [Fact]
    public void A_built_rule_with_no_integration_trait_is_reported()
    {
        var report = RuleCoverage.Analyse(Ids("SELL-1"), Traits(UnitProject, "SELL-1"), Ids());

        Assert.Single(report.MissingCoverage, gap => gap.StartsWith("SELL-1: no integration-test trait", StringComparison.Ordinal));
    }

    [Fact]
    public void A_trait_naming_an_id_not_in_the_requirements_is_reported()
    {
        var report = RuleCoverage.Analyse(Ids("SELL-1"), Traits(IntegrationProject, "SELL-9"), Ids("SELL-1"));

        Assert.Equal(new[] { $"{IntegrationProject}: SELL-9" }, report.UnknownTraits);
    }

    [Fact]
    public void A_pending_id_that_already_has_a_trait_is_reported_as_stale()
    {
        var report = RuleCoverage.Analyse(Ids("SELL-1"), Traits(IntegrationProject, "SELL-1"), Ids("SELL-1"));

        Assert.Equal(new[] { "SELL-1" }, report.StalePending);
    }

    [Fact]
    public void A_pending_id_not_in_the_requirements_is_reported()
    {
        var report = RuleCoverage.Analyse(Ids("SELL-1"), Traits(IntegrationProject), Ids("SELL-1", "SELL-99"));

        Assert.Equal(new[] { "SELL-99" }, report.UnknownPending);
    }

    [Fact]
    public void Trait_parsing_reads_only_Rule_trait_literals()
    {
        // Built by concatenation so this file holds no trait-shaped literal for the scan to find.
        var source = "[" + "Trait(\"Rule\", \"SELL-2\")]\n[" + "Trait( \"Rule\" , \"SELL-5\" )]\n[" + "Trait(\"Category\", \"SELL-7\")]";

        Assert.Equal(new[] { "SELL-2", "SELL-5" }, RuleCoverage.ParseTraits(source));
    }

    private static IReadOnlySet<string> Ids(params string[] ids) => ids.ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Traits(string project, params string[] ids) =>
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal) { [project] = Ids(ids) };
}
