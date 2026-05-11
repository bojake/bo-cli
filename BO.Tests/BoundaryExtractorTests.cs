using BO.Core.Configuration;
using BO.Core.Indexing;

namespace BO.Tests;

public sealed class BoundaryExtractorTests
{
    [Fact]
    public void Extract_ClassifiesExternalPackagesAgainstBoundaryRules()
    {
        using var fixture = FixtureWorkspace.Create("partial-semantic");
        var scanner = new WorkspaceScanner(new BO.Core.Ids.BoIdGenerator());
        var rules = new ArtifactLoader().LoadPackageClassificationRules(
            Path.Combine(fixture.WorkspaceRoot, "package_classification_rules.json"));
        var scanResult = scanner.Scan(fixture.WorkspaceRoot, rules.Version);
        var extractor = new BoundaryExtractor();

        var interactions = extractor.Extract(scanResult.Files, rules);

        Assert.Contains(interactions, interaction =>
            interaction.BoundaryType == "http" &&
            interaction.TargetName == "axios.get" &&
            interaction.OperationType == "read");
        Assert.Contains(interactions, interaction =>
            interaction.BoundaryType == "db" &&
            interaction.TargetName == "client.query" &&
            interaction.OperationType == "read");
    }
}
