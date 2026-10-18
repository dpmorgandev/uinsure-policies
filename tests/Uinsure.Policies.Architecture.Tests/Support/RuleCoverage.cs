using System.Text.RegularExpressions;

namespace Uinsure.Policies.Architecture.Tests.Support;

internal sealed record RuleCoverageReport(
    IReadOnlyList<string> MissingCoverage,
    IReadOnlyList<string> UnknownTraits,
    IReadOnlyList<string> StalePending,
    IReadOnlyList<string> UnknownPending);

// Works from source text. U: the rule ids in the first column of the §2 rule tables of
// docs/requirements.md (X-1/2 expands to X-1 and X-2). T: the Rule trait literals in tests/**/*.cs,
// keyed by test project. P: tests/rule-coverage.pending.txt, the ids of nodes not yet built.
internal static partial class RuleCoverage
{
    public static RuleCoverageReport ForRepository(string root)
    {
        var ruleIds = ParseRuleIds(File.ReadAllText(Path.Combine(root, "docs", "requirements.md")));
        var pending = ParsePending(File.ReadAllText(Path.Combine(root, "tests", "rule-coverage.pending.txt")));
        var testsDirectory = Path.Combine(root, "tests");

        var traitsByProject = Directory.EnumerateFiles(testsDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(file => (File: file, Relative: Path.GetRelativePath(testsDirectory, file).Replace('\\', '/')))
            .Where(source => !RepoPaths.IsBuildOutput(source.Relative))
            .GroupBy(source => source.Relative.Split('/')[0], StringComparer.Ordinal)
            .ToDictionary(
                project => project.Key,
                project => (IReadOnlySet<string>)project
                    .SelectMany(source => ParseTraits(File.ReadAllText(source.File)))
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        return Analyse(ruleIds, traitsByProject, pending);
    }

    public static IReadOnlySet<string> ParseRuleIds(string requirementsMarkdown)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        var inFunctionalRequirements = false;

        foreach (var rawLine in requirementsMarkdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inFunctionalRequirements = line.StartsWith("## 2.", StringComparison.Ordinal);
            }

            var row = RuleRow().Match(line);
            if (inFunctionalRequirements && row.Success)
            {
                ids.UnionWith(Expand(row.Groups[1].Value));
            }
        }

        return ids;
    }

    public static IEnumerable<string> ParseTraits(string source) =>
        RuleTrait().Matches(source).Select(match => match.Groups[1].Value);

    public static IReadOnlySet<string> ParsePending(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToHashSet(StringComparer.Ordinal);

    public static RuleCoverageReport Analyse(
        IReadOnlySet<string> ruleIds,
        IReadOnlyDictionary<string, IReadOnlySet<string>> traitsByProject,
        IReadOnlySet<string> pending)
    {
        var unit = TraitsIn(traitsByProject, IsUnitProject);
        var integration = TraitsIn(traitsByProject, IsIntegrationProject);
        var anywhere = TraitsIn(traitsByProject, _ => true);

        var missing = ruleIds.Where(id => !pending.Contains(id))
            .SelectMany(id => Gaps(id, unit, integration))
            .ToList();
        var unknownTraits = traitsByProject
            .SelectMany(project => project.Value.Where(id => !ruleIds.Contains(id)).Select(id => $"{project.Key}: {id}"))
            .Order(StringComparer.Ordinal)
            .ToList();
        var stale = pending.Where(anywhere.Contains).Order(StringComparer.Ordinal).ToList();
        var unknownPending = pending.Where(id => !ruleIds.Contains(id)).Order(StringComparer.Ordinal).ToList();

        return new RuleCoverageReport(missing, unknownTraits, stale, unknownPending);
    }

    private static bool IsUnitProject(string project) =>
        project.EndsWith(".Domain.Tests", StringComparison.Ordinal) || project.EndsWith(".Services.Tests", StringComparison.Ordinal);

    private static bool IsIntegrationProject(string project) =>
        project.EndsWith(".Api.Tests", StringComparison.Ordinal);

    private static HashSet<string> TraitsIn(
        IReadOnlyDictionary<string, IReadOnlySet<string>> traitsByProject,
        Func<string, bool> projectFilter) =>
        traitsByProject.Where(project => projectFilter(project.Key))
            .SelectMany(project => project.Value)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> Gaps(string id, HashSet<string> unit, HashSet<string> integration)
    {
        if (!unit.Contains(id))
        {
            yield return $"{id}: no unit-test trait (Domain.Tests or Services.Tests)";
        }

        if (!integration.Contains(id))
        {
            yield return $"{id}: no integration-test trait (Api.Tests)";
        }
    }

    private static IEnumerable<string> Expand(string cell)
    {
        var slash = cell.IndexOf('/');
        if (slash < 0)
        {
            return [cell];
        }

        var prefix = cell[..(cell.LastIndexOf('-', slash) + 1)];
        return [cell[..slash], prefix + cell[(slash + 1)..]];
    }

    [GeneratedRegex(@"^\|\s*([A-Z]+-\d+[a-z]?(?:/\d+)?)\s*\|")]
    private static partial Regex RuleRow();

    [GeneratedRegex(@"\[Trait\(\s*""Rule""\s*,\s*""([^""]*)""\s*\)\]")]
    private static partial Regex RuleTrait();
}
