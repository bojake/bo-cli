using BO.Core.Ids;
using BO.Core.Indexing;

namespace BO.Tests;

public sealed class DependencyExtractorTests
{
    [Fact]
    public void Extract_ResolvesRelativeTypeScriptImports()
    {
        using var fixture = FixtureWorkspace.Create("small-ts-service");
        var scanner = new WorkspaceScanner(new BoIdGenerator());
        var result = scanner.Scan(fixture.WorkspaceRoot, "0.1.0");
        var extractor = new DependencyExtractor(new BoIdGenerator());

        var dependencies = extractor.Extract(result.Files);

        Assert.Contains(dependencies, dependency =>
            dependency.ImportText == "./core/greeter" &&
            result.Files.Single(file => file.Id == dependency.ToFileId).NormalizedPath == "src/core/greeter.ts");
        Assert.Contains(dependencies, dependency =>
            dependency.ImportText == "./http/handlers" &&
            result.Files.Single(file => file.Id == dependency.ToFileId).NormalizedPath == "src/http/handlers.ts");
    }

    [Fact]
    public void Extract_ResolvesCommonJsRequireDependencies()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, "lib"));
            File.WriteAllText(Path.Combine(workspaceRoot, "lib", "dep.js"), "module.exports = { value: 1 };");
            File.WriteAllText(Path.Combine(workspaceRoot, "lib", "index.js"), "const dep = require('./dep'); module.exports = dep;");

            var scanner = new WorkspaceScanner(new BoIdGenerator());
            var scanResult = scanner.Scan(workspaceRoot, "0.1.0");
            var extractor = new DependencyExtractor(new BoIdGenerator());

            var dependencies = extractor.Extract(scanResult.Files);

            Assert.Single(dependencies);
            Assert.Equal("./dep", dependencies[0].ImportText);
            Assert.True(dependencies[0].IsRuntime);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "bo-dependency-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
