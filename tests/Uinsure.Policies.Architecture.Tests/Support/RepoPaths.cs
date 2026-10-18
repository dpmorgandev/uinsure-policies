namespace Uinsure.Policies.Architecture.Tests.Support;

internal static class RepoPaths
{
    private const string SolutionFile = "Uinsure.Policies.slnx";

    public static string Root { get; } = FindRoot();

    public static string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    public static string Relative(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    public static bool IsBuildOutput(string relativePath) =>
        relativePath.Split('/', '\\').Any(segment => segment is "bin" or "obj");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"{SolutionFile} was not found above {AppContext.BaseDirectory}.");
    }
}
