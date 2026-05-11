using BO.Core.Configuration;
using BO.Core.Ids;
using BO.Core.Indexing;
using BO.Core.Persistence;
using BO.Core.Persistence.InMemory;
using BO.Core.Services.Index;

namespace BO.Tests;

public sealed class InMemoryGraphStoreTests
{
    [Fact]
    public async Task IndexService_PersistsRepoModuleAndFileGraph()
    {
        var workspaceRoot = CreateTempWorkspace();

        try
        {
            File.WriteAllText(
                Path.Combine(workspaceRoot, "package_classification_rules.json"),
                File.ReadAllText(Path.Combine(GetWorkspaceRoot(), "package_classification_rules.json")));
            File.WriteAllText(Path.Combine(workspaceRoot, "bo_schema.json"), "{}");
            File.WriteAllText(Path.Combine(workspaceRoot, "scoring_config.json"), "{}");
            File.WriteAllText(Path.Combine(workspaceRoot, "refactor_decision_rules.json"), "{}");
            File.WriteAllText(Path.Combine(workspaceRoot, "workspace_scan_rules.json"), "{}");
            File.WriteAllText(Path.Combine(workspaceRoot, "semantic_profile_rules.json"), "{}");
            File.WriteAllText(Path.Combine(workspaceRoot, "architecture_placement_rules.json"), "{}");

            Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
            File.WriteAllText(Path.Combine(workspaceRoot, "src", "index.ts"), "export const value = 1;");

            var store = new InMemoryGraphStore();
            var service = new IndexWorkspaceService(
                new ArtifactLoader(),
                new WorkspaceScanner(new BoIdGenerator()),
                new SourceSymbolExtractor(new BoIdGenerator()),
                new ContractExtractor(),
                new DependencyExtractor(new BoIdGenerator()),
                new SymbolDependencyExtractor(),
                new BoundaryExtractor(),
                new EffectProfileDeriver(),
                new ComplexityProfileDeriver(),
                new ResponsibilityProfileDeriver(),
                new ContextBurdenDeriver(),
                new RefactorPressureScorer(),
                new RefactorDecisionDeriver(),
                new SeamExtractionPlanner(),
                store);

            var result = await service.IndexAsync(workspaceRoot);

            var repoNode = await store.GetNodeByIdAsync(result.Repo.Id);
            var repoEdges = await store.GetOutgoingEdgesAsync(result.Repo.Id);
            var fileEdges = await store.GetOutgoingEdgesAsync(result.Files[0].Id);

            Assert.NotNull(store.Schema);
            Assert.NotNull(repoNode);
            Assert.Equal("Repo", repoNode!.Label);
            Assert.NotEmpty(repoEdges);
            Assert.NotEmpty(result.Symbols);
            Assert.Contains(repoEdges, edge => edge.Label == "CONTAINS_FILE");
            Assert.Contains(repoEdges, edge => edge.Label == "CONTAINS_MODULE");
            Assert.Contains(fileEdges, edge => edge.Label == "DEFINES_SYMBOL");
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyWriteBatch_ReplacesExistingNodeById()
    {
        var store = new InMemoryGraphStore();
        await store.EnsureSchemaAsync(GraphStoreSchemas.BoV01);

        await store.ApplyWriteBatchAsync(new GraphWriteBatch(
            [new GraphNodeRecord("repo:test", "Repo", new Dictionary<string, object?> { ["name"] = "first" })],
            []));
        await store.ApplyWriteBatchAsync(new GraphWriteBatch(
            [new GraphNodeRecord("repo:test", "Repo", new Dictionary<string, object?> { ["name"] = "second" })],
            []));

        var node = await store.GetNodeByIdAsync("repo:test");

        Assert.NotNull(node);
        Assert.Equal("second", node!.Properties["name"]);
    }

    private static string GetWorkspaceRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "bo-graph-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
