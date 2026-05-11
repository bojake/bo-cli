namespace BO.Tests;

internal sealed class FixtureWorkspace : IDisposable
{
    public string FixtureName { get; }
    public string WorkspaceRoot { get; }

    private FixtureWorkspace(string fixtureName, string workspaceRoot)
    {
        FixtureName = fixtureName;
        WorkspaceRoot = workspaceRoot;
    }

    public static FixtureWorkspace Create(string fixtureName)
    {
        var fixtureSourceRoot = Path.Combine(GetRepositoryRoot(), "BO.Tests", "Fixtures", fixtureName);
        if (!Directory.Exists(fixtureSourceRoot))
        {
            throw new DirectoryNotFoundException($"Fixture '{fixtureName}' was not found at '{fixtureSourceRoot}'.");
        }

        var workspaceRoot = Path.Combine(Path.GetTempPath(), $"bo-fixture-{fixtureName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);

        CopyDirectory(fixtureSourceRoot, workspaceRoot);
        CopyRequiredArtifacts(GetRepositoryRoot(), workspaceRoot);

        return new FixtureWorkspace(fixtureName, workspaceRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(WorkspaceRoot, recursive: true);
        }
        catch
        {
            // Best effort cleanup for temp fixture workspaces.
        }
    }

    public static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static void CopyRequiredArtifacts(string repositoryRoot, string workspaceRoot)
    {
        foreach (var fileName in new[] { "package_classification_rules.json", "scoring_config.json", "refactor_decision_rules.json", "workspace_scan_rules.json", "semantic_profile_rules.json", "architecture_placement_rules.json", "bo_schema.json" })
        {
            File.Copy(
                Path.Combine(repositoryRoot, fileName),
                Path.Combine(workspaceRoot, fileName),
                overwrite: true);
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".golden.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }
}
