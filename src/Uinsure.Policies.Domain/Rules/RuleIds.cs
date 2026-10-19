namespace Uinsure.Policies.Domain.Rules;

public static class RuleIds
{
    public const string Get1 = "GET-1";
    public const string Sell1 = "SELL-1";
    public const string Sell2 = "SELL-2";
    public const string Sell3 = "SELL-3";
    public const string Sell4 = "SELL-4";
    public const string Sell5 = "SELL-5";
    public const string Sell6 = "SELL-6";
    public const string Sell7 = "SELL-7";
    public const string Cancel1a = "CANCEL-1a";
    public const string Cancel1b = "CANCEL-1b";
    public const string Cancel1c = "CANCEL-1c";
    public const string Cancel3 = "CANCEL-3";
    public const string Cancel4 = "CANCEL-4";
    public const string Cancel7 = "CANCEL-7";
    public const string Renew12 = "RENEW-1/2";
    public const string Renew3b = "RENEW-3b";
    public const string Renew5 = "RENEW-5";
    public const string Renew6 = "RENEW-6";
    public const string Could3 = "COULD-3";
    public const string Could2 = "COULD-2";
    public const string Could2a = "COULD-2a";
    public const string Could2b = "COULD-2b";
    public const string Could2c = "COULD-2c";

    // requirements.md order: it decides which violation is "first" and how a result lists them.
    public static IReadOnlyList<string> Order { get; } = [Get1, Sell1, Sell2, Sell3, Sell4, Sell5, Sell6, Sell7, Cancel1a, Cancel1b, Cancel1c, Cancel3, Cancel4, Cancel7, Renew12, Renew3b, Renew5, Renew6, Could2, Could2a, Could2b, Could2c, Could3];
}
