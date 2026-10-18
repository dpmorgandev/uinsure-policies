namespace Uinsure.Policies.Domain.Rules;

public static class RuleIds
{
    public const string Sell1 = "SELL-1";
    public const string Sell2 = "SELL-2";
    public const string Sell3 = "SELL-3";
    public const string Sell4 = "SELL-4";
    public const string Sell5 = "SELL-5";
    public const string Sell6 = "SELL-6";
    public const string Sell7 = "SELL-7";

    // requirements.md order: it decides which violation is "first" and how a result lists them.
    public static IReadOnlyList<string> Order { get; } = [Sell1, Sell2, Sell3, Sell4, Sell5, Sell6, Sell7];
}
