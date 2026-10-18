namespace Uinsure.Policies.Persistence;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
}

public sealed class DatabaseOptions
{
    // Kept as the configured text so a typo is reported at start-up instead of silently defaulting.
    public string? Provider { get; set; }

    public DatabaseProvider ProviderKind => Enum.Parse<DatabaseProvider>(Provider!, ignoreCase: true);

    public static bool IsValid(DatabaseOptions options) =>
        Enum.TryParse<DatabaseProvider>(options.Provider, ignoreCase: true, out var kind) && Enum.IsDefined(kind);
}
