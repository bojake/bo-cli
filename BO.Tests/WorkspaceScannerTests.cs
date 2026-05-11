using BO.Core.Ids;
using BO.Core.Configuration;
using BO.Core.Indexing;

namespace BO.Tests;

public sealed class WorkspaceScannerTests
{
    [Fact]
    public void Scan_ExcludesNodeModulesAndBuildOutputs()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "node_modules", "pkg"));
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "dist"));

            File.WriteAllText(Path.Combine(workspaceRoot, "src", "index.ts"), "export const value = 1;");
            File.WriteAllText(Path.Combine(workspaceRoot, "node_modules", "pkg", "skip.ts"), "export const skip = 1;");
            File.WriteAllText(Path.Combine(workspaceRoot, "dist", "skip.js"), "export const skip = 1;");

            var scanner = new WorkspaceScanner(new BoIdGenerator());
            var result = scanner.Scan(workspaceRoot, "0.1.0");

            Assert.Single(result.Files);
            Assert.EndsWith("src/index.ts", result.Files[0].NormalizedPath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public void Scan_AppliesRepoConfigurationExcludesAndGeneratedBoundaries()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src", "generated"));
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "scratch"));

            File.WriteAllText(Path.Combine(workspaceRoot, "src", "index.ts"), "export const value = 1;");
            File.WriteAllText(Path.Combine(workspaceRoot, "src", "generated", "api.ts"), "export const generated = 1;");
            File.WriteAllText(Path.Combine(workspaceRoot, "scratch", "skip.ts"), "export const skip = 1;");

            var config = BoConfiguration.Empty with
            {
                Boundaries =
                [
                    new BoBoundaryConfiguration(
                        "generated",
                        "Generated code.",
                        ["src/generated/**"],
                        Generated: true)
                ],
                Indexing = BoConfiguration.Empty.Indexing with
                {
                    ExcludePathPatterns = ["scratch/**"]
                }
            };

            var scanner = new WorkspaceScanner(new BoIdGenerator());
            var result = scanner.Scan(workspaceRoot, "0.1.0", boConfiguration: config);

            Assert.DoesNotContain(result.Files, file => file.NormalizedPath.Contains("scratch/", StringComparison.Ordinal));
            Assert.Contains(result.Files, file => file.NormalizedPath == "src/index.ts" && !file.IsGenerated);
            Assert.Contains(result.Files, file => file.NormalizedPath == "src/generated/api.ts" && file.IsGenerated);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public void Scan_UsesConfiguredScannerConventions()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "vendor"));

            File.WriteAllText(Path.Combine(workspaceRoot, "src", "index.ts"), "export const value = 1;");
            File.WriteAllText(Path.Combine(workspaceRoot, "src", "index.check.ts"), "export const test = 1;");
            File.WriteAllText(Path.Combine(workspaceRoot, "src", "schema.fixture.ts"), "export const generated = 1;");
            File.WriteAllText(Path.Combine(workspaceRoot, "vendor", "skip.ts"), "export const skip = 1;");

            var scanRules = WorkspaceScanRules.Default with
            {
                ExcludedDirectories = ["vendor"],
                TestPathRules =
                [
                    new WorkspacePathRule("default", [], [".check.ts"])
                ],
                GeneratedPathRules =
                [
                    new WorkspacePathRule("default", [], [".fixture.ts"])
                ]
            };

            var scanner = new WorkspaceScanner(new BoIdGenerator());
            var result = scanner.Scan(workspaceRoot, "0.1.0", scanRules);

            Assert.DoesNotContain(result.Files, file => file.NormalizedPath.Contains("vendor/", StringComparison.Ordinal));
            Assert.Contains(result.Files, file => file.NormalizedPath == "src/index.ts" && !file.IsTest && !file.IsGenerated);
            Assert.Contains(result.Files, file => file.NormalizedPath == "src/index.check.ts" && file.IsTest);
            Assert.Contains(result.Files, file => file.NormalizedPath == "src/schema.fixture.ts" && file.IsGenerated);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "bo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
